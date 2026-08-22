using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using WorkflowApp.Application.Common.Events;
using WorkflowApp.Application.Common.Interfaces;

namespace WorkflowApp.Api.Hubs;

/// <summary>
/// The real-time channel. It carries notifications only — every method here is about which groups a
/// connection belongs to, and nothing about it can change application state. Commands go through
/// the REST API, where the permission checks live.
///
/// Group membership is derived from the token on connect, so a client cannot subscribe itself into
/// a feed it has no permission for. Reconnects re-run <see cref="OnConnectedAsync"/>, which is what
/// makes recovery work: the connection is re-placed in its groups, and the client re-fetches the
/// state it may have missed while it was away.
/// </summary>
[Authorize]
public sealed class WorkflowHub : Hub
{
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<WorkflowHub> _logger;

    public WorkflowHub(ICurrentUser currentUser, ILogger<WorkflowHub> logger)
    {
        _currentUser = currentUser;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        if (_currentUser.UserId is { } userId)
            await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.User(userId));

        // Permission groups come from the token, never from the client.
        foreach (var permission in _currentUser.Permissions)
            await Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.Permission(permission));

        _logger.LogDebug(
            "Hub connection {ConnectionId} opened for user {UserId}", Context.ConnectionId, _currentUser.UserId);

        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        // SignalR removes the connection from its groups on disconnect; nothing to undo by hand.
        _logger.LogDebug("Hub connection {ConnectionId} closed", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>Starts receiving updates for one task — call it when the task screen opens.</summary>
    public Task SubscribeToTask(long taskId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, RealtimeGroups.Task(taskId));

    public Task UnsubscribeFromTask(long taskId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, RealtimeGroups.Task(taskId));
}
