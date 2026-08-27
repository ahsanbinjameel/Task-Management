using Microsoft.AspNetCore.SignalR;
using WorkflowApp.Api.Hubs;
using WorkflowApp.Application.Common;
using WorkflowApp.Application.Common.Events;

namespace WorkflowApp.Api.Services;

/// <summary>
/// Maps integration events onto SignalR groups.
///
/// This is the only place that decides who hears about what, so the routing rules can be read in
/// one sitting rather than inferred from scattered <c>SendAsync</c> calls. Each event goes to the
/// people with a legitimate interest and no further: a task update reaches whoever has it open, the
/// person it is assigned to, and the coordinators who schedule work — not everyone logged in.
/// </summary>
public sealed class SignalRIntegrationEventPublisher : IIntegrationEventPublisher
{
    private readonly IHubContext<WorkflowHub> _hub;

    public SignalRIntegrationEventPublisher(IHubContext<WorkflowHub> hub) => _hub = hub;

    public async Task PublishAsync(IReadOnlyList<IntegrationEvent> events, CancellationToken ct = default)
    {
        foreach (var @event in events)
        {
            var groups = Recipients(@event).Distinct().ToList();
            if (groups.Count == 0) continue;

            await _hub.Clients.Groups(groups).SendAsync(@event.Channel, @event, ct);
        }
    }

    private static IEnumerable<string> Recipients(IntegrationEvent @event)
    {
        switch (@event)
        {
            case TaskChangedEvent task:
                yield return RealtimeGroups.Task(task.TaskId);
                if (task.AssigneeUserId is { } assignee)
                    yield return RealtimeGroups.User(assignee);
                yield return RealtimeGroups.Permission(Permissions.TaskAssign);
                yield return RealtimeGroups.Permission(Permissions.WorkforceViewAll);
                break;

            case RequestChangedEvent request:
                yield return RealtimeGroups.User(request.RequesterUserId);
                yield return RealtimeGroups.Permission(Permissions.TaskReview);
                break;

            // Narrower than a task deliberately. A verification concerns the checker holding it,
            // whoever raised it, and the reviewers waiting on the answer — the assignment
            // coordinators have no part in it, because verifications are not scheduled work.
            case VerificationChangedEvent verification:
                yield return RealtimeGroups.Verification(verification.VerificationId);
                if (verification.AssignedToUserId is { } checker)
                    yield return RealtimeGroups.User(checker);
                yield return RealtimeGroups.Permission(Permissions.VerificationViewAll);
                yield return RealtimeGroups.Permission(Permissions.TaskReview);
                break;

            case WorkforceChangedEvent workforce:
                yield return RealtimeGroups.User(workforce.UserId);
                yield return RealtimeGroups.Permission(Permissions.WorkforceViewAll);
                break;

            // Strictly personal — a notification is addressed to one person by definition.
            case NotificationRaisedEvent notification:
                yield return RealtimeGroups.User(notification.RecipientUserId);
                break;
        }
    }
}
