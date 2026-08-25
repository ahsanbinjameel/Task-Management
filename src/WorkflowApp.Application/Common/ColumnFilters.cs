using System.Globalization;

namespace WorkflowApp.Application.Common;

/// <summary>
/// The per-column filters a grid sends, as <c>column key → typed value</c>.
///
/// A dictionary rather than a property per column, because the filter row is generated from the
/// column list: adding a column to a grid should not mean adding a property to a query record, a
/// parameter to a controller action and a line to a client-side interface. The service that owns
/// the table decides what each key means — nothing here knows about titles or clients.
///
/// **Unknown keys are ignored, not rejected.** A stale bookmark or a column removed from a grid
/// must show a sensible list rather than an error; there is no security consequence, because a key
/// nobody handles simply does not filter anything.
///
/// Values arrive as strings because they come off a query string. The readers below are the only
/// place that changes, and each returns null when the value is absent, blank or unparseable — so a
/// half-typed date narrows nothing instead of emptying the grid.
/// </summary>
public sealed class ColumnFilters
{
    public static readonly ColumnFilters None = new(new Dictionary<string, string?>());

    private readonly IReadOnlyDictionary<string, string?> _values;

    public ColumnFilters(IReadOnlyDictionary<string, string?>? values) =>
        _values = values is null
            ? new Dictionary<string, string?>()
            : new Dictionary<string, string?>(values, StringComparer.OrdinalIgnoreCase);

    public bool Any => _values.Values.Any(v => !string.IsNullOrWhiteSpace(v));

    /// <summary>Trimmed text, or null when nothing usable was sent.</summary>
    public string? Text(string key) =>
        _values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;

    public long? Id(string key) =>
        long.TryParse(Text(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            ? id
            : null;

    public bool? Bool(string key) =>
        bool.TryParse(Text(key), out var value) ? value : null;

    /// <summary>
    /// An enum member by name. The client sends names rather than ordinals — the API serialises
    /// them that way everywhere else, and an ordinal in a URL silently means something different
    /// the day a member is inserted.
    /// </summary>
    public T? Enum<T>(string key) where T : struct, Enum =>
        System.Enum.TryParse<T>(Text(key), ignoreCase: true, out var value) ? value : null;

    /// <summary>A whole day, as the local calendar date the user picked.</summary>
    public DateOnly? Date(string key) =>
        DateOnly.TryParse(Text(key), CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
}
