namespace WorkflowApp.Application.Common.Options;

/// <summary>Bound from the <c>Workforce</c> configuration section.</summary>
public sealed class WorkforceOptions
{
    public const string SectionName = "Workforce";

    /// <summary>
    /// The business time zone. All timestamps are stored in UTC; this only decides where a
    /// "day" starts and ends for timelines and daily reports. Accepts a Windows id
    /// ("India Standard Time") or an IANA id ("Asia/Karachi") — .NET 8 resolves both on Windows.
    /// </summary>
    public string TimeZoneId { get; set; } = "UTC";

    /// <summary>
    /// A shift open longer than this was almost certainly abandoned — the user closed the browser
    /// without ending it. The sweep closes those and flags them as improperly ended.
    /// </summary>
    public int MaxShiftHours { get; set; } = 16;

    /// <summary>Whether the background sweep runs. Turn it off in environments that share a database.</summary>
    public bool AutoCloseStaleShifts { get; set; } = true;

    /// <summary>How often the sweep runs.</summary>
    public int StaleShiftScanMinutes { get; set; } = 30;
}
