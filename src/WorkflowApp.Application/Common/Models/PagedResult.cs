namespace WorkflowApp.Application.Common.Models;

/// <summary>A page of results plus the totals a client needs to render pagination.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

/// <summary>Normalised paging input. Clamps hostile or careless values.</summary>
public sealed record PageQuery
{
    private const int MaxPageSize = 200;

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;

    public int NormalizedPage => Page < 1 ? 1 : Page;
    public int NormalizedPageSize => PageSize switch
    {
        < 1 => 25,
        > MaxPageSize => MaxPageSize,
        _ => PageSize
    };

    public int Skip => (NormalizedPage - 1) * NormalizedPageSize;
}

/// <summary>
/// How many records sit in each status, for the tiles that sit above a list.
///
/// Counted over the same filters as the list itself minus the status filter, so the numbers agree
/// with what clicking a tile will show. Counting the unfiltered table instead is the usual bug: the
/// tile promises 12 and the list then shows 3.
/// </summary>
/// <summary>
/// One tile above a list. <paramref name="Key"/> is the view it selects — a stable, URL-safe name
/// for a group of internal statuses, not the status itself, because which statuses belong together
/// depends on who is looking.
/// </summary>
public sealed record StatusCountDto(string Key, string Label, int Count);
