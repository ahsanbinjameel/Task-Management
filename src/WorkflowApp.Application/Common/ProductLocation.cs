namespace WorkflowApp.Application.Common;

/// <summary>
/// The product-catalog axis, as one line a person reads: <c>Sales · Delivery Order · Detail Report</c>.
///
/// The <em>model</em> keeps module, form and surface as three separate nullable columns, which is
/// what makes the cross-client questions in PRODUCT-CORE §5 answerable — "every Delivery Order
/// detail-report issue across all clients" is a query over those columns. What nobody reads is
/// three fields stacked in a panel, so every screen shows the joined form and this is the one place
/// that joins it.
///
/// Deliberately not stored. It is a rendering of three ids, and a stored copy would be wrong the
/// first time a form was renamed.
/// </summary>
public static class ProductLocation
{
    public const string Separator = " · ";

    /// <summary>
    /// The coarsest-to-finest parts that are actually known, joined. Null when none is set, so a
    /// caller can leave the whole line out rather than printing an empty label — most requests
    /// arrive with none of this, and it is filled in at triage.
    /// </summary>
    public static string? Format(string? moduleName, string? formName, string? surfaceName)
    {
        var parts = new[] { moduleName, formName, surfaceName }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Trim())
            .ToArray();

        return parts.Length == 0 ? null : string.Join(Separator, parts);
    }
}
