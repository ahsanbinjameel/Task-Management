using WorkflowApp.Domain.Entities.Identity;

namespace WorkflowApp.Application.Demo;

/// <summary>One of the demo cast, as the switcher shows them.</summary>
public sealed record DemoUserDto(
    long Id,
    string UserName,
    string DisplayName,
    /// <summary>The single role this member of the cast exists to demonstrate.</summary>
    string Role,
    /// <summary>What that role is for, in a sentence, so the switcher explains itself.</summary>
    string Purpose);

/// <summary>Whether a demonstration can be run, and whether one is running.</summary>
public sealed record DemoStatusDto(
    bool IsAvailable,
    bool IsActive,
    /// <summary>The demo account currently being shown, when one is.</summary>
    string? CurrentUserName,
    /// <summary>The real account that started it, so exiting can name where it returns to.</summary>
    string? RealUserName,
    IReadOnlyList<DemoUserDto> Cast);

/// <summary>
/// The demo catalog: the same schema, the same migrations and the same code, in a database live
/// never opens.
///
/// This exists because the demo database is the one thing the rest of the application cannot reach
/// through its usual door. Every other service takes <c>IWorkflowDbContext</c>, which points at
/// whichever catalog the caller's token selected — so a live session preparing a demonstration, or
/// a demo session being reset, has no way to talk to the other side. This does, and only this does.
///
/// What it deliberately is not: a place for demo-flavoured business logic. Nothing here decides what
/// a request is or how triage works; it creates the cast, hands back what a token needs, and empties
/// the catalog on request. Everything a demonstration then shows runs through the ordinary services
/// against the ordinary schema, which is the entire point of the design.
/// </summary>
public interface IDemoEnvironment
{
    /// <summary>
    /// False when no demo catalog could be resolved. The feature then simply is not offered, rather
    /// than being offered and failing at the moment somebody clicks it in front of an audience.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Bring the demo catalog up: create it if absent, migrate it to the current model, seed the
    /// permission catalogue and the cast, and give them something to look at.
    ///
    /// Idempotent, and called on every entry rather than once at startup — a demo database somebody
    /// dropped by hand between demonstrations should heal on the next click, not require a restart.
    /// </summary>
    Task EnsureReadyAsync(CancellationToken ct = default);

    /// <summary>The cast, in the order the switcher shows them: the shape of the workflow.</summary>
    Task<IReadOnlyList<DemoUserDto>> CastAsync(CancellationToken ct = default);

    /// <summary>
    /// One of the cast, with the roles and permissions a token needs. Null when the id is not one
    /// of them — which is what stops the switcher being pointed at an arbitrary row.
    /// </summary>
    Task<DemoPrincipal?> FindAsync(long demoUserId, CancellationToken ct = default);

    /// <summary>
    /// Empty the demo catalog and rebuild it.
    ///
    /// Safe by construction rather than by care: it can only ever address the demo connection, so
    /// there is no argument anybody could pass that would make it touch live data.
    /// </summary>
    Task ResetAsync(CancellationToken ct = default);
}

/// <summary>Everything needed to mint a token for a demo user.</summary>
public sealed record DemoPrincipal(
    User User,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);
