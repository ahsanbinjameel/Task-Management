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

    /// <summary>
    /// The same filters with one column dropped.
    ///
    /// Used to work out what a column's dropdown should still offer: a column is always computed
    /// against every filter *except* its own, or ticking one value would erase the rest of that
    /// column's choices and multi-select could never get past its first pick.
    /// </summary>
    public ColumnFilters Without(string key) =>
        new(_values.Where(kv => !kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                   .ToDictionary(kv => kv.Key, kv => kv.Value));

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

    /// <summary>
    /// Several values for one column: <c>col[priority]=Critical|High</c>.
    ///
    /// **The separator is a pipe, and it has to be.** A comma is the obvious choice and does not
    /// survive: ASP.NET's query value provider treats a comma-separated value as several values for
    /// the same key, and the dictionary binder then keeps exactly one of them — so
    /// <c>Critical,High</c> silently arrived as <c>High</c> and the grid filtered by the wrong
    /// thing without erroring. A repeated key (<c>col[x]=a&amp;col[x]=b</c>) fails the same way,
    /// keeping only the first. A pipe passes through untouched.
    ///
    /// Only ever used for tokens that cannot contain the separator — enum names and numeric ids.
    /// Free text is deliberately never split: a filter is one term, and guessing which side of a
    /// separator the user meant is worse than not guessing.
    /// </summary>
    public IReadOnlyList<string> Many(string key) =>
        Text(key)?.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        ?? Array.Empty<string>();

    /// <summary>The ids that parsed. A malformed one narrows nothing rather than emptying the grid.</summary>
    public IReadOnlyList<long> Ids(string key) =>
        Many(key)
            .Select(v => long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
                ? id
                : (long?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToList();

    /// <summary>The enum members that parsed, by name. See <see cref="Enum{T}"/> for why names.</summary>
    public IReadOnlyList<T> Enums<T>(string key) where T : struct, Enum =>
        Many(key)
            .Select(v => System.Enum.TryParse<T>(v, ignoreCase: true, out var value) ? value : (T?)null)
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToList();

    /// <summary>A whole day, as the local calendar date the user picked.</summary>
    public DateOnly? Date(string key) =>
        DateOnly.TryParse(Text(key), CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
}
