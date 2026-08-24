using WorkflowApp.Domain.Enums;

namespace WorkflowApp.Application.Common;

/// <summary>
/// The words users see for internal status names.
///
/// The enum names are for the code and the database; they are not English. Showing
/// "CompletedReadyForQC" to someone who does not work on the system is showing them our schema.
/// This is the single place that translates, so the server and the UI cannot drift apart in what
/// they call the same state.
///
/// Kept deliberately plain: "Waiting to be given out" beats "ReadyForAssignment" for a reader who
/// has never used workflow software.
///
/// The client mirrors this map in <c>client/src/app/core/labels.ts</c>. The two must stay in step:
/// a state named one thing in an API message and another in a template is worse than either name
/// on its own. Change a label here and there in the same edit.
/// </summary>
public static class StatusLabels
{
    private static readonly IReadOnlyDictionary<WorkTaskStatus, string> TaskLabels =
        new Dictionary<WorkTaskStatus, string>
        {
            [WorkTaskStatus.Requested] = "Requested",
            [WorkTaskStatus.AwaitingReview] = "Waiting for review",
            [WorkTaskStatus.ClarificationRequired] = "Needs information",
            [WorkTaskStatus.Approved] = "Approved",
            [WorkTaskStatus.ReadyForAssignment] = "Waiting to be given out",
            [WorkTaskStatus.Assigned] = "Assigned",
            [WorkTaskStatus.ReadyToStart] = "Ready to start",
            [WorkTaskStatus.InProgress] = "In progress",
            [WorkTaskStatus.Paused] = "Paused",
            [WorkTaskStatus.Blocked] = "Cannot continue",
            [WorkTaskStatus.CompletedReadyForQC] = "Waiting for quality check",
            [WorkTaskStatus.QCReview] = "Being checked",
            [WorkTaskStatus.QCFailedRework] = "Needs fixing",
            [WorkTaskStatus.QCPassed] = "Passed the check",
            [WorkTaskStatus.ReadyForClosure] = "Ready to close",
            [WorkTaskStatus.Closed] = "Closed",
            [WorkTaskStatus.Cancelled] = "Cancelled",
            [WorkTaskStatus.Deferred] = "Postponed",
            [WorkTaskStatus.OnHold] = "On hold",
            [WorkTaskStatus.Duplicate] = "Duplicate",
            [WorkTaskStatus.Reopened] = "Opened again",
        };

    private static readonly IReadOnlyDictionary<RequestStatus, string> RequestLabels =
        new Dictionary<RequestStatus, string>
        {
            [RequestStatus.Submitted] = "Waiting for review",
            [RequestStatus.InReview] = "Being reviewed",
            [RequestStatus.ClarificationRequired] = "Needs information",
            [RequestStatus.Approved] = "Approved",
            [RequestStatus.Rejected] = "Rejected",
            [RequestStatus.Duplicate] = "Duplicate",
            [RequestStatus.Deferred] = "Postponed",
            [RequestStatus.Escalated] = "Escalated",
        };

    public static string For(WorkTaskStatus status) =>
        TaskLabels.TryGetValue(status, out var label) ? label : Humanise(status.ToString());

    public static string For(RequestStatus status) =>
        RequestLabels.TryGetValue(status, out var label) ? label : Humanise(status.ToString());

    /// <summary>Last resort for a value added to the enum but not to the map above.</summary>
    private static string Humanise(string name)
    {
        var spaced = System.Text.RegularExpressions.Regex
            .Replace(name, "(?<=[a-z])(?=[A-Z])", " ");
        return char.ToUpperInvariant(spaced[0]) + spaced[1..].ToLowerInvariant();
    }
}
