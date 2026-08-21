using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkflowApp.Application.Common.Options;

namespace WorkflowApp.Application.Common.Services;

/// <summary>
/// Converts between UTC instants and business days.
///
/// Everything is stored in UTC, but "today's timeline" and "the daily report" are business-day
/// concepts: a shift running 22:00–06:00 local must land on the day the employee would say it did.
/// Resolving the day boundary in the configured zone — rather than slicing UTC at midnight — is
/// what makes those reports match the timesheet.
/// </summary>
public interface IBusinessCalendar
{
    TimeZoneInfo TimeZone { get; }

    /// <summary>The business date a UTC instant falls on.</summary>
    DateOnly ToBusinessDate(DateTimeOffset instant);

    /// <summary>The UTC half-open interval [start, end) covering one business day.</summary>
    (DateTimeOffset Start, DateTimeOffset EndExclusive) DayRange(DateOnly date);

    /// <summary>The instant rendered in the business zone, for display.</summary>
    DateTimeOffset ToBusinessTime(DateTimeOffset instant);
}

public sealed class BusinessCalendar : IBusinessCalendar
{
    public BusinessCalendar(IOptions<WorkforceOptions> options, ILogger<BusinessCalendar> logger)
    {
        var id = options.Value.TimeZoneId;

        try
        {
            TimeZone = TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            // Falling back beats refusing to start: reports shift by an offset, which is visible
            // and correctable, whereas a dead application is not.
            logger.LogError(ex,
                "Workforce:TimeZoneId '{TimeZoneId}' could not be resolved. Falling back to UTC — " +
                "daily timelines and reports will use UTC day boundaries until this is fixed.", id);
            TimeZone = TimeZoneInfo.Utc;
        }
    }

    public TimeZoneInfo TimeZone { get; }

    public DateOnly ToBusinessDate(DateTimeOffset instant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, TimeZone).DateTime);

    public DateTimeOffset ToBusinessTime(DateTimeOffset instant) =>
        TimeZoneInfo.ConvertTime(instant, TimeZone);

    public (DateTimeOffset Start, DateTimeOffset EndExclusive) DayRange(DateOnly date)
    {
        var start = StartOfDay(date);
        // Computed from the next date rather than start + 24h, so a DST transition inside the day
        // produces a 23- or 25-hour range instead of silently clipping or overlapping.
        var end = StartOfDay(date.AddDays(1));
        return (start, end);
    }

    private DateTimeOffset StartOfDay(DateOnly date)
    {
        var local = date.ToDateTime(TimeOnly.MinValue);

        // Spring-forward: local midnight may not exist. Step forward to the first instant that does.
        if (TimeZone.IsInvalidTime(local))
            local = local.AddHours(1);

        var offset = TimeZone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }
}
