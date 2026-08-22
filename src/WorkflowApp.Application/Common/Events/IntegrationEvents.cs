using WorkflowApp.Domain.Enums;

namespace WorkflowApp.Application.Common.Events;

/// <summary>
/// Something worth telling connected clients about. Payloads are deliberately thin: an identifier,
/// the new state, and what kind of change it was. The database is the source of truth, so a client
/// re-fetches what it needs — a fat payload would be a second, staler copy of the record and would
/// go wrong the moment a client applied it out of order.
/// </summary>
public abstract record IntegrationEvent
{
    /// <summary>The SignalR client method this event is delivered on.</summary>
    public abstract string Channel { get; }
}

public sealed record TaskChangedEvent(
    long TaskId,
    string TaskNumber,
    WorkTaskStatus Status,
    long? AssigneeUserId,
    ChangeKind Kind) : IntegrationEvent
{
    public override string Channel => "taskChanged";
}

public sealed record RequestChangedEvent(
    long RequestId,
    string RequestNumber,
    RequestStatus Status,
    long RequesterUserId,
    ChangeKind Kind) : IntegrationEvent
{
    public override string Channel => "requestChanged";
}

public sealed record WorkforceChangedEvent(
    long UserId,
    WorkforceState State) : IntegrationEvent
{
    public override string Channel => "workforceChanged";
}

public sealed record NotificationRaisedEvent(
    long RecipientUserId,
    long NotificationId,
    string Title) : IntegrationEvent
{
    public override string Channel => "notification";
}

public enum ChangeKind
{
    Created = 0,
    Updated = 1
}

/// <summary>
/// Group names. Shared so the hub that joins a group and the publisher that sends to it cannot
/// drift apart — a typo on one side would be a silently undelivered notification.
/// </summary>
public static class RealtimeGroups
{
    /// <summary>Everything addressed to one person, across all their open tabs.</summary>
    public static string User(long userId) => $"user:{userId}";

    /// <summary>Whoever currently has this task open.</summary>
    public static string Task(long taskId) => $"task:{taskId}";

    /// <summary>
    /// Everyone holding a permission. Joined at connection time from the token's claims, which is
    /// what lets "the assignment queue changed" reach exactly the coordinators.
    /// </summary>
    public static string Permission(string permissionKey) => $"perm:{permissionKey}";
}

/// <summary>
/// Collects events raised during a unit of work. Scoped, so it is per-request.
/// </summary>
public interface IIntegrationEventQueue
{
    void Enqueue(IntegrationEvent @event);

    /// <summary>Takes everything queued so far and empties the queue.</summary>
    IReadOnlyList<IntegrationEvent> Drain();
}

/// <summary>
/// Ships events to connected clients. Implemented in the API layer, where SignalR lives.
/// </summary>
public interface IIntegrationEventPublisher
{
    Task PublishAsync(IReadOnlyList<IntegrationEvent> events, CancellationToken ct = default);
}

public sealed class IntegrationEventQueue : IIntegrationEventQueue
{
    private readonly List<IntegrationEvent> _events = new();

    public void Enqueue(IntegrationEvent @event) => _events.Add(@event);

    public IReadOnlyList<IntegrationEvent> Drain()
    {
        if (_events.Count == 0) return Array.Empty<IntegrationEvent>();

        var drained = _events.ToList();
        _events.Clear();
        return drained;
    }
}

/// <summary>
/// The default when nothing is listening — tests, and any host that does not run SignalR.
/// Publishing is a side channel; nothing in the domain should fail because it is absent.
/// </summary>
public sealed class NullIntegrationEventPublisher : IIntegrationEventPublisher
{
    public Task PublishAsync(IReadOnlyList<IntegrationEvent> events, CancellationToken ct = default) =>
        Task.CompletedTask;
}
