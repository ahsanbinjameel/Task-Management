using Microsoft.Extensions.Options;
using WorkflowApp.Application.Common.Options;
using WorkflowApp.Application.Workforce.Services;

namespace WorkflowApp.Api.Services;

/// <summary>
/// Periodically closes shifts that were never ended — the "user closed the browser and went home"
/// case. The work itself lives in <see cref="IShiftMaintenanceService"/>; this class only decides
/// when to run it.
///
/// It fails soft on purpose: a sweep that throws must not take the web host down with it, so every
/// iteration is wrapped and the loop continues to the next interval.
/// </summary>
public sealed class StaleShiftSweepService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WorkforceOptions _options;
    private readonly ILogger<StaleShiftSweepService> _logger;

    public StaleShiftSweepService(
        IServiceScopeFactory scopeFactory,
        IOptions<WorkforceOptions> options,
        ILogger<StaleShiftSweepService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.AutoCloseStaleShifts)
        {
            _logger.LogInformation("Stale-shift sweep is disabled (Workforce:AutoCloseStaleShifts = false).");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, _options.StaleShiftScanMinutes));

        _logger.LogInformation(
            "Stale-shift sweep running every {Interval}, closing shifts open longer than {MaxShiftHours}h.",
            interval, _options.MaxShiftHours);

        using var timer = new PeriodicTimer(interval);

        // Run once at startup: a crash or restart is exactly when shifts get orphaned.
        await SweepAsync(stoppingToken);

        while (await SafeWaitAsync(timer, stoppingToken))
            await SweepAsync(stoppingToken);
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var maintenance = scope.ServiceProvider.GetRequiredService<IShiftMaintenanceService>();

            var closed = await maintenance.CloseStaleShiftsAsync(ct);
            if (closed > 0)
                _logger.LogWarning("Stale-shift sweep closed {Count} abandoned shift(s).", closed);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Host shutting down — not an error.
        }
        catch (Exception ex)
        {
            // Most likely the database is unreachable. Log and try again next interval.
            _logger.LogError(ex, "Stale-shift sweep failed; will retry on the next interval.");
        }
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
