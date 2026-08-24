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
    IReadOnlyList<SupportedTaskDto> SupportingOn);

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
