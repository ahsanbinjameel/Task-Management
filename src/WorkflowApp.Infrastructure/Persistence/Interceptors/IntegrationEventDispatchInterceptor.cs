using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using WorkflowApp.Application.Common.Events;
using WorkflowApp.Domain.Entities.Identity;
using WorkflowApp.Domain.Entities.Requests;
using WorkflowApp.Domain.Entities.Tasks;
using WorkflowApp.Domain.Entities.Verifications;

namespace WorkflowApp.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Turns committed changes into real-time notifications.
///
/// Events are derived from the change tracker rather than raised by hand in each service. That is
/// the whole point: no code path can forget to notify, because notification is a consequence of the
/// write itself. And it is genuinely <b>after commit</b> — nothing is sent for a save that rolled
/// back, so a client can never be told about a state the database never reached.
///
/// Publishing failures are logged and swallowed. SignalR only notifies; the database is the source
/// of truth, and a dropped notification must never fail the transaction that caused it.
/// </summary>
public sealed class IntegrationEventDispatchInterceptor : SaveChangesInterceptor
{
    private readonly IIntegrationEventQueue _queue;
    private readonly IIntegrationEventPublisher _publisher;
    private readonly ILogger<IntegrationEventDispatchInterceptor> _logger;

    public IntegrationEventDispatchInterceptor(
        IIntegrationEventQueue queue,
        IIntegrationEventPublisher publisher,
        ILogger<IntegrationEventDispatchInterceptor> logger)
    {
        _queue = queue;
        _publisher = publisher;
        _logger = logger;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        // Collected before the save, because afterwards every entry reads as Unchanged.
        Collect(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        Collect(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        var events = _queue.Drain();

        if (events.Count > 0)
        {
            try
            {
                await _publisher.PublishAsync(events, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish {Count} integration event(s)", events.Count);
            }
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        // The write did not happen, so neither did anything worth announcing.
        _queue.Drain();
        base.SaveChangesFailed(eventData);
    }

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        _queue.Drain();
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    // --- collection ----------------------------------------------------------------------

    private void Collect(DbContext? context)
    {
        if (context is null) return;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified)) continue;

            switch (entry.Entity)
            {
                case WorkTask task:
                    _queue.Enqueue(new TaskChangedEvent(
                        task.Id, task.TaskNumber, task.Status, task.PrimaryAssigneeUserId, Kind(entry)));
                    break;

                case Verification verification:
                    _queue.Enqueue(new VerificationChangedEvent(
                        verification.Id, verification.VerificationNumber, verification.Status,
                        verification.AssignedToUserId, Kind(entry)));
                    break;

                case Request request:
                    _queue.Enqueue(new RequestChangedEvent(
                        request.Id, request.RequestNumber, request.Status,
                        request.RequestedByUserId, Kind(entry)));
                    break;

                // Only availability matters to a watcher; a password change is nobody's business.
                case User user when entry.State == EntityState.Modified &&
                                    entry.Property(nameof(User.WorkforceState)).IsModified:
                    _queue.Enqueue(new WorkforceChangedEvent(user.Id, user.WorkforceState));
                    break;

                case Notification notification when entry.State == EntityState.Added:
                    _queue.Enqueue(new NotificationRaisedEvent(
                        notification.RecipientUserId, notification.Id, notification.Title));
                    break;
            }
        }
    }

    private static ChangeKind Kind(EntityEntry entry) =>
        entry.State == EntityState.Added ? ChangeKind.Created : ChangeKind.Updated;
}
