using WorkflowApp.Domain.Enums;

namespace WorkflowApp.Application.Common;

/// <summary>Who a list is being drawn for. Decides how much of the workflow they are shown.</summary>
public enum StatusAudience
{
    /// <summary>Asked for the work. Wants to know what is happening, not how the system models it.</summary>
    Requester = 0,

    /// <summary>Does the work. Wants their own next action.</summary>
    Worker = 1,

    /// <summary>Runs the work. Needs the operational detail the other two are spared.</summary>
    Coordinator = 2,
}

/// <summary>
/// One tile above a list: a key that survives a URL, a label for a human, and the internal
/// statuses behind it.
/// </summary>
public sealed record TaskStatusView(string Key, string Label, IReadOnlyList<WorkTaskStatus> Statuses);

/// <summary>
/// A request's tile. A request keeps its own status until it is approved and then stops moving —
/// everything after that happens on the task — so a view is matched against whichever of the two
/// is the live one.
/// </summary>
public sealed record RequestStatusView(
    string Key,
    string Label,
    IReadOnlyList<RequestStatus> RequestStatuses,
    IReadOnlyList<WorkTaskStatus> TaskStatuses);

/// <summary>
/// Internal workflow states, grouped into the few each audience actually needs.
///
/// The state machine stays exactly as it is: twenty-two task states earn their place because the
/// rules need them. What nobody needs is to be *shown* twenty-two of them. A requester asking
/// "is my report fixed yet?" is not helped by the difference between `CompletedReadyForQC` and
/// `QCReview`, and a worker does not care that work nobody has been given yet sits in
/// `ReadyForAssignment`.
///
/// So this is the one place that decides which states collapse together and what the group is
/// called. It lives on the server because the *filter* has to run in the database — counting
/// tiles on the client would only ever count the page you can already see — and because two
/// copies of this table would drift within a month.
/// </summary>
public static class StatusViews
{
    public const string AllKey = "all";

    /// <summary>
    /// Which grouping a signed-in user gets, decided by what they are allowed to do rather than
    /// by a role name — roles are only bundles, and a site that renames them must not change what
    /// anyone sees.
    ///
    /// Coordinating beats working beats asking: someone who both assigns and executes needs the
    /// operational detail, so the widest permission wins.
    /// </summary>
    public static StatusAudience AudienceFor(IReadOnlySet<string> permissions)
    {
        if (permissions.Contains(Permissions.TaskAssign)
            || permissions.Contains(Permissions.TaskReview)
            || permissions.Contains(Permissions.TaskQCReview)
            || permissions.Contains(Permissions.DashboardManagement))
            return StatusAudience.Coordinator;

        return permissions.Contains(Permissions.TaskWork)
            ? StatusAudience.Worker
            : StatusAudience.Requester;
    }

    // ---- tasks -------------------------------------------------------------------------------

    /// <summary>
    /// The worker's day: what to pick up, what is running, what is stuck, what came back, what is
    /// being checked, what is finished. Work waiting to be handed out is deliberately absent —
    /// it is nobody's yet, so it is not part of anyone's personal navigation.
    /// </summary>
    private static readonly IReadOnlyList<TaskStatusView> WorkerViews = new[]
    {
        new TaskStatusView("todo", "To Do", new[]
        {
            WorkTaskStatus.Assigned, WorkTaskStatus.ReadyToStart, WorkTaskStatus.Reopened,
        }),
        new TaskStatusView("working", "Working", new[] { WorkTaskStatus.InProgress }),
        new TaskStatusView("waiting", "Waiting", new[]
        {
            WorkTaskStatus.Paused, WorkTaskStatus.Blocked,
            WorkTaskStatus.OnHold, WorkTaskStatus.Deferred,
        }),
        new TaskStatusView("fixing", "Needs Fixing", new[] { WorkTaskStatus.QCFailedRework }),
        new TaskStatusView("checking", "Quality Check", new[]
        {
            WorkTaskStatus.CompletedReadyForQC, WorkTaskStatus.QCReview,
        }),
        new TaskStatusView("done", "Done", new[]
        {
            WorkTaskStatus.QCPassed, WorkTaskStatus.ReadyForClosure, WorkTaskStatus.Closed,
            WorkTaskStatus.Cancelled, WorkTaskStatus.Duplicate,
        }),
    };

    /// <summary>
    /// The coordinator's board. Paused and Blocked stay apart here because the difference is the
    /// whole point of the role: paused is someone's own pace, blocked is something to go and fix.
    /// </summary>
    private static readonly IReadOnlyList<TaskStatusView> CoordinatorViews = new[]
    {
        new TaskStatusView("unassigned", "Waiting for Assignment", new[] { WorkTaskStatus.ReadyForAssignment }),
        new TaskStatusView("assigned", "Assigned", new[]
        {
            WorkTaskStatus.Assigned, WorkTaskStatus.ReadyToStart, WorkTaskStatus.Reopened,
        }),
        new TaskStatusView("working", "In Progress", new[] { WorkTaskStatus.InProgress }),
        new TaskStatusView("paused", "Paused", new[] { WorkTaskStatus.Paused }),
        new TaskStatusView("blocked", "Blocked", new[] { WorkTaskStatus.Blocked }),
        new TaskStatusView("checking", "Quality Check", new[]
        {
            WorkTaskStatus.CompletedReadyForQC, WorkTaskStatus.QCReview,
        }),
        new TaskStatusView("fixing", "Needs Fixing", new[] { WorkTaskStatus.QCFailedRework }),
        new TaskStatusView("passed", "Quality Check Passed", new[]
        {
            WorkTaskStatus.QCPassed, WorkTaskStatus.ReadyForClosure,
        }),
        new TaskStatusView("closed", "Closed", new[] { WorkTaskStatus.Closed }),
        new TaskStatusView("stopped", "Not Doing", new[]
        {
            WorkTaskStatus.Cancelled, WorkTaskStatus.Duplicate,
            WorkTaskStatus.Deferred, WorkTaskStatus.OnHold,
        }),
    };

    /// <summary>
    /// A requester looking at the task side (they can reach a task they raised) gets the same
    /// plain-language grouping their requests use.
    /// </summary>
    private static readonly IReadOnlyList<TaskStatusView> RequesterTaskViews = new[]
    {
        new TaskStatusView("approved", "Approved", new[] { WorkTaskStatus.ReadyForAssignment }),
        new TaskStatusView("assigned", "Assigned", new[]
        {
            WorkTaskStatus.Assigned, WorkTaskStatus.ReadyToStart,
        }),
        new TaskStatusView("working", "In Progress", new[]
        {
            WorkTaskStatus.InProgress, WorkTaskStatus.Paused,
            WorkTaskStatus.QCFailedRework, WorkTaskStatus.Reopened,
        }),
        new TaskStatusView("waiting", "Waiting", new[]
        {
            WorkTaskStatus.Blocked, WorkTaskStatus.OnHold, WorkTaskStatus.Deferred,
        }),
        new TaskStatusView("checking", "Being Checked", new[]
        {
            WorkTaskStatus.CompletedReadyForQC, WorkTaskStatus.QCReview,
        }),
        new TaskStatusView("done", "Completed", new[]
        {
            WorkTaskStatus.QCPassed, WorkTaskStatus.ReadyForClosure, WorkTaskStatus.Closed,
        }),
        new TaskStatusView("declined", "Not Approved", new[]
        {
            WorkTaskStatus.Cancelled, WorkTaskStatus.Duplicate,
        }),
    };

    public static IReadOnlyList<TaskStatusView> ForTasks(StatusAudience audience) => audience switch
    {
        StatusAudience.Coordinator => CoordinatorViews,
        StatusAudience.Worker => WorkerViews,
        _ => RequesterTaskViews,
    };

    public static TaskStatusView? FindTaskView(StatusAudience audience, string? key) =>
        string.IsNullOrWhiteSpace(key) || key.Equals(AllKey, StringComparison.OrdinalIgnoreCase)
            ? null
            : ForTasks(audience).FirstOrDefault(v => v.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    /// <summary>The group a task falls in for this audience — what its chip should say.</summary>
    public static TaskStatusView? ViewOf(StatusAudience audience, WorkTaskStatus status) =>
        ForTasks(audience).FirstOrDefault(v => v.Statuses.Contains(status));

    // ---- requests ----------------------------------------------------------------------------

    /// <summary>
    /// What the person who asked for the work sees. Ten words for twenty-nine internal states.
    ///
    /// Two foldings are deliberate. Paused reads as In Progress: someone stepping away for lunch
    /// is not news to the requester, and a status that flickers with the worker's day would only
    /// prompt "why has it stopped?". Failed quality check also reads as In Progress: the work came
    /// back to the same person and is moving again — "Needs Fixing" would invite the requester to
    /// chase something that is already being handled.
    /// </summary>
    private static readonly IReadOnlyList<RequestStatusView> RequesterViews = new[]
    {
        new RequestStatusView("submitted", "Submitted",
            new[] { RequestStatus.Submitted }, Array.Empty<WorkTaskStatus>()),
        new RequestStatusView("review", "Under Review",
            new[] { RequestStatus.InReview, RequestStatus.Escalated }, Array.Empty<WorkTaskStatus>()),
        new RequestStatusView("input", "Needs Your Input",
            new[] { RequestStatus.ClarificationRequired }, Array.Empty<WorkTaskStatus>()),
        new RequestStatusView("approved", "Approved",
            new[] { RequestStatus.Approved }, new[] { WorkTaskStatus.ReadyForAssignment }),
        new RequestStatusView("assigned", "Assigned",
            Array.Empty<RequestStatus>(),
            new[] { WorkTaskStatus.Assigned, WorkTaskStatus.ReadyToStart }),
        new RequestStatusView("working", "In Progress",
            Array.Empty<RequestStatus>(),
            new[]
            {
                WorkTaskStatus.InProgress, WorkTaskStatus.Paused,
                WorkTaskStatus.QCFailedRework, WorkTaskStatus.Reopened,
            }),
        new RequestStatusView("waiting", "Waiting",
            new[] { RequestStatus.Deferred },
            new[] { WorkTaskStatus.Blocked, WorkTaskStatus.OnHold, WorkTaskStatus.Deferred }),
        new RequestStatusView("checking", "Being Checked",
            Array.Empty<RequestStatus>(),
            new[] { WorkTaskStatus.CompletedReadyForQC, WorkTaskStatus.QCReview }),
        new RequestStatusView("done", "Completed",
            Array.Empty<RequestStatus>(),
            new[]
            {
                WorkTaskStatus.QCPassed, WorkTaskStatus.ReadyForClosure, WorkTaskStatus.Closed,
            }),
        new RequestStatusView("declined", "Not Approved",
            new[] { RequestStatus.Rejected, RequestStatus.Duplicate },
            new[] { WorkTaskStatus.Cancelled, WorkTaskStatus.Duplicate }),
    };

    /// <summary>
    /// Reviewers and coordinators keep the intake states in full — telling Submitted from
    /// In Review from Needs Information is their job, and the task side is not their concern here.
    /// </summary>
    private static readonly IReadOnlyList<RequestStatusView> ReviewerViews = new[]
    {
        new RequestStatusView("submitted", "Waiting for review",
            new[] { RequestStatus.Submitted }, Array.Empty<WorkTaskStatus>()),
        new RequestStatusView("review", "Being reviewed",
            new[] { RequestStatus.InReview }, Array.Empty<WorkTaskStatus>()),
        new RequestStatusView("input", "Needs information",
            new[] { RequestStatus.ClarificationRequired }, Array.Empty<WorkTaskStatus>()),
        new RequestStatusView("approved", "Approved",
            new[] { RequestStatus.Approved }, Array.Empty<WorkTaskStatus>()),
        new RequestStatusView("escalated", "Escalated",
            new[] { RequestStatus.Escalated }, Array.Empty<WorkTaskStatus>()),
        new RequestStatusView("declined", "Not approved",
            new[] { RequestStatus.Rejected, RequestStatus.Duplicate }, Array.Empty<WorkTaskStatus>()),
        new RequestStatusView("waiting", "Postponed",
            new[] { RequestStatus.Deferred }, Array.Empty<WorkTaskStatus>()),
    };

    /// <summary>
    /// Whether this audience's request status follows the generated task.
    ///
    /// **The single answer to that question.** It was decided independently in four places — the
    /// view table, the label, the list filter and the tile counts — and any two of them disagreeing
    /// produces a silently empty screen, which is exactly what happened twice.
    ///
    /// The test is "does this person triage work?", not "do they do work?". Only a coordinator
    /// stops at intake, because approving is where their involvement ends. Everyone else reading a
    /// request is reading their own, and for them the request is the record of the work: once it is
    /// approved the request itself stops moving and only the task has anything left to say.
    ///
    /// A **worker** counts as a requester here, which is the whole reason this exists. The Worker
    /// role holds <c>Request.Create</c> deliberately — someone who fields a call and finds real
    /// work behind it has to be able to raise it — but <see cref="AudienceFor"/> classifies from
    /// *task* permissions, so their own request was being read with the reviewer's table and froze
    /// on "Approved" forever.
    /// </summary>
    public static bool RequestStatusFollowsTask(StatusAudience audience) =>
        audience != StatusAudience.Coordinator;

    public static IReadOnlyList<RequestStatusView> ForRequests(StatusAudience audience) =>
        RequestStatusFollowsTask(audience) ? RequesterViews : ReviewerViews;

    public static RequestStatusView? FindRequestView(StatusAudience audience, string? key) =>
        string.IsNullOrWhiteSpace(key) || key.Equals(AllKey, StringComparison.OrdinalIgnoreCase)
            ? null
            : ForRequests(audience).FirstOrDefault(v => v.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The one line a requester should read on their own request. The task decides once there is
    /// one, because after approval the request itself stops moving.
    /// </summary>
    public static RequestStatusView RequestViewOf(
        StatusAudience audience, RequestStatus status, WorkTaskStatus? taskStatus)
    {
        var views = ForRequests(audience);

        if (taskStatus is { } live && RequestStatusFollowsTask(audience))
        {
            var byTask = views.FirstOrDefault(v => v.TaskStatuses.Contains(live));
            if (byTask is not null) return byTask;
        }

        return views.FirstOrDefault(v => v.RequestStatuses.Contains(status))
            // Nothing in the map: show the enum rather than an empty chip. Reachable only if a
            // status is added to the domain and not to a view here.
            ?? new RequestStatusView(
                status.ToString().ToLowerInvariant(), StatusLabels.For(status),
                new[] { status }, Array.Empty<WorkTaskStatus>());
    }
}
