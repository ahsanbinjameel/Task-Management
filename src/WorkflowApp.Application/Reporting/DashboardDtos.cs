using WorkflowApp.Domain.Enums;

namespace WorkflowApp.Application.Reporting;

/// <summary>What the person who raised the work needs to see: where their asks got to.</summary>
public sealed record RequesterDashboardDto(
    int SubmittedCount,
    int UnderReviewCount,
    int AwaitingMyClarificationCount,
    int InProgressCount,
    int ClosedCount,
    int RejectedCount,
    IReadOnlyList<DashboardItemDto> Recent);

/// <summary>What the person doing the work needs: what is on me, and what is stuck.</summary>
public sealed record WorkerDashboardDto(
    int QueueLength,
    int InProgressCount,
    int BlockedCount,
    int ReworkCount,
    int OverdueCount,
    long? ActiveTaskId,
    string? ActiveTaskNumber,
    bool IsOnShift,
    TimeSpan WorkedToday,
    int UnreadNotifications,
    IReadOnlyList<DashboardItemDto> Queue);

/// <summary>What the coordinator needs: what is unassigned, stuck or late.</summary>
public sealed record CoordinatorDashboardDto(
    int AwaitingReviewCount,
    int UnassignedCount,
    int BlockedCount,
    int AwaitingQCCount,
    int OverdueCount,
    int PeopleOnShift,
    int PeopleWorking,
    IReadOnlyList<DashboardItemDto> Unassigned,
    IReadOnlyList<DashboardItemDto> Overdue);

/// <summary>What management needs: whether the system as a whole is keeping up.</summary>
public sealed record ManagementDashboardDto(
    DateOnly From,
    DateOnly To,
    int RequestsRaised,
    int TasksCreated,
    int TasksClosed,
    int QCAttempts,
    int QCFailures,
    double QCPassRate,
    double? AverageCycleTimeHours,
    decimal TotalHoursWorked,
    int OpenTaskCount,
    int OverdueCount,
    IReadOnlyList<CountByLabelDto> OpenByStatus,
    IReadOnlyList<CountByLabelDto> OpenByPriority,
    IReadOnlyList<CountByLabelDto> ClosedByAssignee);

public sealed record CountByLabelDto(string Label, int Count);

// --- the home dashboard: what to do, and what has happened -------------------------------------

/// <summary>
/// The dashboard split in two, because a screen that mixes them serves neither half well.
///
/// <see cref="NeedsAttention"/> is a to-do list: every row is something *this caller* can act on
/// now, with the reason it is here and how long it has been waiting. <see cref="RecentActivity"/>
/// is news: things that happened around their work which they may want to know but need do
/// nothing about. Before the split, a closed task and a task waiting three days for someone to
/// pick it up sat in the same list looking equally urgent.
/// </summary>
public sealed record HomeDashboardDto(
    IReadOnlyList<AttentionItemDto> NeedsAttention,
    IReadOnlyList<ActivityItemDto> RecentActivity,
    /// <summary>The full count before the list was truncated, so the page can say "and 14 more".</summary>
    int TotalNeedingAttention);

/// <summary>What kind of record a row points at, so the client can build the right link.</summary>
public enum AttentionSubject
{
    Task = 0,
    Request = 1,
}

/// <summary>
/// One thing waiting on the caller. <paramref name="Reason"/> is written for a person and is the
/// point of the row: "Needs someone to do it" is actionable where "ReadyForAssignment" is not.
/// </summary>
public sealed record AttentionItemDto(
    AttentionSubject Subject,
    long Id,
    string Number,
    string Title,
    string Reason,
    /// <summary>Ranks the list. Lower sorts first. Not shown.</summary>
    int Rank,
    Priority Priority,
    /// <summary>When it entered the state that put it here — the basis for "waiting 3 days".</summary>
    DateTimeOffset Since,
    DateTimeOffset? DueDate,
    bool IsOverdue);

/// <summary>Something that happened. Past tense, no action attached.</summary>
public sealed record ActivityItemDto(
    AttentionSubject Subject,
    long Id,
    string Number,
    string Text,
    DateTimeOffset At);

/// <summary>A task or request reduced to what a dashboard tile needs.</summary>
public sealed record DashboardItemDto(
    long Id,
    string Number,
    string Title,
    string Status,
    Priority Priority,
    DateTimeOffset? DueDate,
    bool IsOverdue);

// --- reports --------------------------------------------------------------------------------

public sealed record DailyUserReportDto(
    DateOnly Date,
    long UserId,
    string DisplayName,
    DateTimeOffset? ShiftStart,
    DateTimeOffset? ShiftEnd,
    TimeSpan ShiftDuration,
    TimeSpan ProductiveTime,
    TimeSpan BreakTime,
    int TasksWorked,
    int TasksCompleted,
    /// <summary>Time on tasks this person is responsible for.</summary>
    IReadOnlyList<TaskTimeDto> OwnedWork,
    /// <summary>
    /// Time on tasks somebody else is responsible for. Reported separately and never added to the
    /// owned figures: helping with a task is not the same as being accountable for it.
    /// </summary>
    IReadOnlyList<TaskTimeDto> SupportWork,
    /// <summary>Tasks they are listed as helping with, whether or not they logged time today.</summary>
    IReadOnlyList<SupportedTaskDto> SupportingOn,
    /// <summary>
    /// Work that never came through the front door: the phone calls and desk visits.
    ///
    /// Reported as its own line rather than folded into either of the two above, because it is
    /// neither. Without it a day reads as six hours of work in an eight-hour shift and nobody can
    /// say where the other two went — which is the complaint that produced Quick Work in the first
    /// place.
    /// </summary>
    IReadOnlyList<QuickWorkLineDto> QuickWork,
    /// <summary>Time on finished quick work. Cancelled records are excluded, deliberately.</summary>
    TimeSpan QuickWorkTime,
    /// <summary>
    /// How many times a running task was put down for something else today. The number people
    /// actually argue about, and until now nothing counted it.
    /// </summary>
    int Interruptions);

/// <summary>One piece of quick work, as a report reads it.</summary>
public sealed record QuickWorkLineDto(
    long Id,
    string Title,
    DateTimeOffset StartedAt,
    TimeSpan Duration,
    string? ClientName,
    string? Outcome,
    /// <summary>The task it displaced, where it displaced one.</summary>
    string? InterruptedTaskNumber,
    /// <summary>Set when it turned out to be real work and a request was raised.</summary>
    string? PromotedToRequestNumber,
    bool WasCancelled);

public sealed record TaskTimeDto(long TaskId, string TaskNumber, string Title, TimeSpan TimeSpent, int Sessions);

/// <summary>A task this person is helping with. The responsible person is named so the report
/// cannot be misread as saying the work belongs to the helper.</summary>
public sealed record SupportedTaskDto(
    long TaskId,
    string TaskNumber,
    string Title,
    string Status,
    string? ResponsiblePersonName);

public sealed record DailyTeamReportDto(
    DateOnly Date,
    int PeopleOnShift,
    TimeSpan TotalShiftTime,
    TimeSpan TotalProductiveTime,
    int TasksCompleted,
    IReadOnlyList<DailyUserReportDto> Users);
