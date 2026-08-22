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
    IReadOnlyList<TaskTimeDto> Breakdown);

public sealed record TaskTimeDto(long TaskId, string TaskNumber, string Title, TimeSpan TimeSpent, int Sessions);

public sealed record DailyTeamReportDto(
    DateOnly Date,
    int PeopleOnShift,
    TimeSpan TotalShiftTime,
    TimeSpan TotalProductiveTime,
    int TasksCompleted,
    IReadOnlyList<DailyUserReportDto> Users);
