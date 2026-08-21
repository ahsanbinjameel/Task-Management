using System.ComponentModel.DataAnnotations;
using WorkflowApp.Domain.Enums;

namespace WorkflowApp.Application.Workforce.Dtos;

public sealed record ChangeWorkforceStateRequest
{
    [Required]
    public WorkforceState State { get; init; }

    [MaxLength(500)]
    public string? Note { get; init; }
}

public sealed record EndShiftRequest
{
    [MaxLength(500)]
    public string? Note { get; init; }
}

public sealed record ForceEndShiftRequest
{
    /// <summary>Mandatory: closing someone else's shift alters their attendance record.</summary>
    [Required, MaxLength(500)]
    public string Reason { get; init; } = default!;
}

public sealed record ShiftSessionDto(
    long Id,
    long UserId,
    string UserDisplayName,
    DateTimeOffset ShiftStart,
    DateTimeOffset? ShiftEnd,
    TimeSpan? Duration,
    bool EndedImproperly,
    long? EndedByUserId,
    string? EndNote);

/// <summary>Everything a client needs to render the shift widget for one user.</summary>
public sealed record WorkforceStatusDto(
    long UserId,
    string UserDisplayName,
    WorkforceState State,
    string StateLabel,
    bool IsOnShift,
    // False for reviewers, coordinators, requesters and management — they are not on the clock,
    // so a client should hide the shift controls entirely rather than offer a call that will 403.
    bool IsShiftTracked,
    DateTimeOffset? StateSince,
    ShiftSessionDto? CurrentShift,
    // States this user may switch to themselves right now — drives the UI's state picker.
    IReadOnlyList<WorkforceState> AvailableStates);

public sealed record ActivityEventDto(
    long Id,
    DateTimeOffset OccurredAt,
    string Label,
    WorkforceState? ResultingState,
    long? RelatedTaskId,
    string? Note);

/// <summary>One contiguous stretch of a single state within a day.</summary>
public sealed record TimelineEntryDto(
    DateTimeOffset From,
    DateTimeOffset To,
    TimeSpan Duration,
    string Label,
    WorkforceState? State,
    long? RelatedTaskId,
    string? Note,
    // True when this stretch is still running — its end is "now", not a recorded event.
    bool IsOpen);

public sealed record DailyTimelineDto(
    long UserId,
    string UserDisplayName,
    DateOnly Date,
    IReadOnlyList<TimelineEntryDto> Entries,
    TimeSpan TotalOnShift,
    TimeSpan TotalProductive,
    TimeSpan TotalAway,
    IReadOnlyDictionary<string, TimeSpan> TimeByState);

/// <summary>A row in the "who's working right now" view.</summary>
public sealed record ActiveWorkerDto(
    long UserId,
    string UserName,
    string DisplayName,
    long? DepartmentId,
    long? TeamId,
    WorkforceState State,
    DateTimeOffset ShiftStart,
    TimeSpan ShiftDuration,
    DateTimeOffset? StateSince,
    TimeSpan? TimeInState);

public sealed record ActiveWorkforceDto(
    DateTimeOffset AsOf,
    int TotalOnShift,
    int Working,
    int Available,
    int Away,
    IReadOnlyList<ActiveWorkerDto> Workers);
