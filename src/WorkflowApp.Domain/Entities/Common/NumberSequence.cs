namespace WorkflowApp.Domain.Entities.Common;

/// <summary>
/// A named counter behind the human-facing reference numbers (REQ-000123, TSK-000120).
///
/// These numbers are printed, emailed and quoted in conversation, so they must be dense and
/// sequential — deriving them from a surrogate key would leak gaps whenever a row is rolled back,
/// and mixing requests and tasks into one identity column would make both sequences jump.
///
/// <see cref="Version"/> is a plain integer rather than a ROWVERSION so the optimistic-concurrency
/// guard behaves identically on every provider.
/// </summary>
public class NumberSequence
{
    /// <summary>Sequence name, e.g. "Request" or "Task".</summary>
    public string Key { get; set; } = default!;

    /// <summary>The value the next caller will receive.</summary>
    public long NextValue { get; set; } = 1;

    /// <summary>Concurrency token: two callers racing for a number, one of them retries.</summary>
    public int Version { get; set; }
}
