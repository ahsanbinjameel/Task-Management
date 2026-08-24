using Microsoft.EntityFrameworkCore;
using WorkflowApp.Application.Common.Interfaces;
using WorkflowApp.Domain.Entities.Common;

namespace WorkflowApp.Application.Common.Services;

/// <summary>Allocates the next human-facing reference number for a sequence.</summary>
public interface INumberGenerator
{
    /// <summary>
    /// Reserves and formats the next number, e.g. <c>REQ-000123</c>. Commits immediately — the
    /// number is consumed even if the caller's own work later fails, which is the right trade:
    /// a gap in the sequence is harmless, a duplicate reference number is not.
    /// </summary>
    Task<string> NextAsync(string key, string prefix, CancellationToken ct = default);
}

public sealed class NumberGenerator : INumberGenerator
{
    /// <summary>Two racing callers should settle in one retry; this is headroom, not an expectation.</summary>
    private const int MaxAttempts = 5;

    private const int Digits = 6;

    private readonly IWorkflowDbContext _db;

    public NumberGenerator(IWorkflowDbContext db) => _db = db;

    public async Task<string> NextAsync(string key, string prefix, CancellationToken ct = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            var sequence = await _db.NumberSequences.FirstOrDefaultAsync(s => s.Key == key, ct);

            if (sequence is null)
            {
                sequence = new NumberSequence { Key = key, NextValue = 1 };
                _db.NumberSequences.Add(sequence);
            }

            var value = sequence.NextValue;
            sequence.NextValue = value + 1;
            sequence.Version++;

            try
            {
                await _db.SaveChangesAsync(ct);
                return $"{prefix}-{value.ToString().PadLeft(Digits, '0')}";
            }
            catch (DbUpdateException) when (attempt < MaxAttempts)
            {
                // Someone else took this number — either the concurrency token lost, or two callers
                // inserted the sequence row at once. Drop the stale state and read it again.
                foreach (var entry in _db.ChangeTracker.Entries<NumberSequence>().ToList())
                {
                    await entry.ReloadAsync(ct);
                    if (entry.State == EntityState.Added)
                        entry.State = EntityState.Detached;
                }
            }
        }
    }
}

/// <summary>Sequence names and their printed prefixes, kept together so they cannot drift apart.</summary>
public static class NumberSequences
{
    public const string Request = "Request";
    public const string RequestPrefix = "REQ";

    public const string Task = "Task";
    public const string TaskPrefix = "TSK";

    // Its own counter, like the others: printed numbers must be dense, and a batch sharing the
    // request sequence would put gaps in both.
    public const string Batch = "Batch";
    public const string BatchPrefix = "BAT";
}
