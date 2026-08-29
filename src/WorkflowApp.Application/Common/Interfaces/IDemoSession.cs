namespace WorkflowApp.Application.Common.Interfaces;

/// <summary>
/// Whether this request is running in demo mode, and on whose behalf.
///
/// Demo mode is <b>the same application against a different database</b>. Not a second
/// implementation, not a sample-data flag threaded through the services, not a set of pretend rows
/// living beside the real ones — one extra claim on the token, which this reads, and a connection
/// string chosen from it.
///
/// That shape is the whole point. Every rule, every query, every state machine and every migration
/// is the one production runs, so a demonstration shows the product rather than a lookalike, and a
/// feature cannot be built for live and forgotten for demo because there is only one of it. The
/// previous attempt at a demo mode in this codebase was removed for being the opposite of this —
/// SQLite, <c>EnsureCreated()</c>, its own branches through four files — and the reasons are worth
/// re-reading in CLAUDE.md §6 before anything here grows.
///
/// The isolation is the database, and it is absolute: demo work is written to a catalog live never
/// opens, so nothing done in a demonstration can reach a real client's record.
/// </summary>
public interface IDemoSession
{
    /// <summary>True when the caller's token says this is a demo session.</summary>
    bool IsActive { get; }

    /// <summary>
    /// The real account behind the demonstration, so exiting can hand them their own session back
    /// and so nothing is ever attributed to a demo user that a real person actually did.
    /// </summary>
    long? RealUserId { get; }

    string? RealUserName { get; }
}
