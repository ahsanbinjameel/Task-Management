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
        // The one tile with something for the requester to *do* (PRODUCT-CORE §7). Work that has
        // passed our own quality check is not finished until the person who asked for it says so
        // on their own instance — "coded and passed QC" and "it is fixed" are genuinely different
        // claims, and only they can make the second one.
        new TaskStatusView("confirm", "Ready for Confirmation", new[]
        {
            WorkTaskStatus.QCPassed, WorkTaskStatus.ReadyForClosure,
        }),
        new TaskStatusView("done", "Completed", new[] { WorkTaskStatus.Closed }),
        new TaskStatusView("declined", "Rejected", new[]
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
            // Under verification folds in here on purpose. To the person who asked, "somebody is
            // establishing whether this is really broken" and "somebody is checking the fix" are
            // the same news: it is in hand and there is nothing for them to do. Telling them apart
            // would mean teaching them what a verification is, which is our vocabulary, not theirs.
            new[] { RequestStatus.UnderVerification },
            new[] { WorkTaskStatus.CompletedReadyForQC, WorkTaskStatus.QCReview }),
        // Split out of "Completed" deliberately: this is the only tile a requester is ever asked
        // to act on, and folding it into the finished pile is what left them waiting to be told
        // something they were the only person who could say.
        new RequestStatusView("confirm", "Ready for Confirmation",
            Array.Empty<RequestStatus>(),
            new[] { WorkTaskStatus.QCPassed, WorkTaskStatus.ReadyForClosure }),
        new RequestStatusView("done", "Completed",
            Array.Empty<RequestStatus>(),
            new[] { WorkTaskStatus.Closed }),
        new RequestStatusView("declined", "Rejected",
            new[] { RequestStatus.Rejected, RequestStatus.Duplicate },
            new[] { WorkTaskStatus.Cancelled, WorkTaskStatus.Duplicate }),
    };

    /// <summary>
    /// Reviewers and coordinators keep the intake states in full — telling Submitted from
    /// In Review from Needs Information is their job, and the requester's single "Under Review"
    /// tile would lose the distinction they act on.
    ///
    /// **Everything after Approved is the same journey the requester sees, in internal words.**
    /// These entries used to carry no task statuses at all, on the reasoning that intake is the
    /// reviewer's concern and it stops at approval. What that produced was a request that reached
    /// "Approved" and then never moved again: the tile it sat in was the last one on the strip, so
    /// the screen said the same thing on the day it was approved and a fortnight later with the
    /// work closed. Anyone reading requests through this table — which is anyone holding a task
    /// permission, including a requester who also checks or works — had no way to answer "where
    /// has it got to" without leaving for the task screen and matching it up by hand.
    ///
    /// This is not the coordinator's task board rebuilt on the request screen. Paused reads as
    /// In Progress and rework reads as In Progress, exactly as they do for the requester, because
    /// the question a request answers is "how far along is what I asked for" — the finer
    /// distinctions a coordinator acts on live on Tasks, where the actions are.
    /// </summary>
    private static readonly IReadOnlyList<RequestStatusView> ReviewerViews = new[]
    {
        new RequestStatusView("submitted", "Waiting for review",
            new[] { RequestStatus.Submitted }, Array.Empty<WorkTaskStatus>()),
        new RequestStatusView("review", "Being reviewed",
            new[] { RequestStatus.InReview }, Array.Empty<WorkTaskStatus>()),
        new RequestStatusView("input", "Needs information",
            new[] { RequestStatus.ClarificationRequired }, Array.Empty<WorkTaskStatus>()),
        // Its own tile, unlike the requester's view: "waiting on a checker" and "waiting on the
        // person who asked" are different queues with different people to chase, and separating
        // them is the reviewer's job.
        new RequestStatusView("verifying", "Being verified",
            new[] { RequestStatus.UnderVerification }, Array.Empty<WorkTaskStatus>()),
        new RequestStatusView("escalated", "Escalated",
            new[] { RequestStatus.Escalated }, Array.Empty<WorkTaskStatus>()),
        // Approved keeps the request status beside the task one as a belt-and-braces pairing.
        // Triage approval always creates a task, so in practice the task side is what answers —
        // but a request left Approved with no task must land somewhere rather than vanish.
        new RequestStatusView("approved", "Approved",
            new[] { RequestStatus.Approved }, new[] { WorkTaskStatus.ReadyForAssignment }),
        new RequestStatusView("assigned", "Assigned",
            Array.Empty<RequestStatus>(),
            new[] { WorkTaskStatus.Assigned, WorkTaskStatus.ReadyToStart, WorkTaskStatus.Reopened }),
        new RequestStatusView("working", "In progress",
            Array.Empty<RequestStatus>(),
            new[]
            {
                WorkTaskStatus.InProgress, WorkTaskStatus.Paused, WorkTaskStatus.QCFailedRework,
            }),
        new RequestStatusView("blocked", "Blocked",
            Array.Empty<RequestStatus>(),
            new[] { WorkTaskStatus.Blocked, WorkTaskStatus.OnHold, WorkTaskStatus.Deferred }),
        new RequestStatusView("checking", "Quality check",
            Array.Empty<RequestStatus>(),
            new[] { WorkTaskStatus.CompletedReadyForQC, WorkTaskStatus.QCReview }),
        new RequestStatusView("passed", "Ready for closure",
            Array.Empty<RequestStatus>(),
            new[] { WorkTaskStatus.QCPassed, WorkTaskStatus.ReadyForClosure }),
        new RequestStatusView("done", "Completed",
            Array.Empty<RequestStatus>(), new[] { WorkTaskStatus.Closed }),
        new RequestStatusView("waiting", "Postponed",
            new[] { RequestStatus.Deferred }, Array.Empty<WorkTaskStatus>()),
        new RequestStatusView("declined", "Rejected",
            new[] { RequestStatus.Rejected, RequestStatus.Duplicate },
            new[] { WorkTaskStatus.Cancelled, WorkTaskStatus.Duplicate }),
    };

    /// <summary>
    /// **A request's status follows its generated task, for every audience.**
    ///
    /// There used to be a <c>RequestStatusFollowsTask(audience)</c> predicate here, answering
    /// "does this person triage work?" with <c>audience != Coordinator</c>. It existed because the
    /// same question had been re-derived independently in four places — the view table, the label,
    /// the list filter and the tile counts — and any two of them disagreeing empties a screen in
    /// silence, which is exactly what happened twice.
    ///
    /// It is gone because the premise underneath it was the real fault. A coordinator did not fold
    /// onto the task *because their table had no task statuses to fold onto* — not because they
    /// were better served by a status that stops dead at approval. Both halves are fixed together:
    /// <see cref="ReviewerViews"/> now carries the journey through to Completed, so folding is
    /// correct for everyone and there is no longer a branch to keep in step.
    ///
    /// The audience still decides **which table** is read — the reviewer keeps the intake states
    /// split, the requester gets them folded into "Under Review" — but no longer decides *whether*
    /// the request tracks the work. That was the half that produced frozen requests: a worker, a
    /// checker or an administrator raising a request is classified from *task* permissions, so
    /// their own request was read with the reviewer's table and froze on "Approved" forever.
    /// </summary>
    public static IReadOnlyList<RequestStatusView> ForRequests(StatusAudience audience) =>
        audience == StatusAudience.Coordinator ? ReviewerViews : RequesterViews;

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

        if (taskStatus is { } live)
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
