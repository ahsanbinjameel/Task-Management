namespace WorkflowApp.Application.Common;

/// <summary>
/// Which values a column's dropdown should still offer, given what the other columns are filtered
/// by.
///
/// This is what people mean by "like Excel": choose a client and the priority list stops offering
/// priorities no row under that client has. Without it a filter row is a set of independent
/// controls that happily lead to an empty grid, and the only way to find out which combinations
/// exist is to try them.
///
/// **A column is computed against every filter except its own.** That is the rule that makes
/// multi-select work: having ticked Critical, the priority list must still show High — otherwise
/// picking a first value would erase the choices needed for a second. Excel does the same thing,
/// and it is the part people miss when they build this.
///
/// Values are raw tokens — enum names and ids, matching what <see cref="ColumnFilters"/> reads
/// back. Labels stay on the client, where the wording layer already lives; the client keeps its own
/// full option list and simply hides the ones this does not mention.
/// </summary>
public sealed record FilterOptionsDto(IReadOnlyDictionary<string, IReadOnlyList<string>> Columns);
