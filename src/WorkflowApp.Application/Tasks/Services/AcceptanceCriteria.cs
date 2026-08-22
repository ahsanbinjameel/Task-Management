using System.Text.Json;
using System.Text.RegularExpressions;
using WorkflowApp.Application.Tasks.Dtos;

namespace WorkflowApp.Application.Tasks.Services;

/// <summary>
/// Acceptance criteria are authored as free text on the task — one criterion per line — and
/// evaluated line by line at QC. Keeping them as text rather than a child table means a reviewer or
/// coordinator can rewrite them without a schema migration; the trade-off is that this class owns
/// the parsing, and criteria indexes are only stable while the text is unchanged.
/// </summary>
public static partial class AcceptanceCriteria
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    /// <summary>Splits the task's criteria text into individual criteria, stripping list markers.</summary>
    public static IReadOnlyList<string> Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();

        return text
            .Split('\n')
            .Select(line => MarkerPrefix().Replace(line.Trim(), string.Empty).Trim())
            .Where(line => line.Length > 0)
            .ToList();
    }

    public static string Serialize(IReadOnlyList<AcceptanceCriterionDto> results) =>
        JsonSerializer.Serialize(results, JsonOptions);

    /// <summary>
    /// Reads back a stored evaluation. Returns empty rather than throwing on malformed content —
    /// a QC record from an older shape must not break the task detail view.
    /// </summary>
    public static IReadOnlyList<AcceptanceCriterionDto> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<AcceptanceCriterionDto>();

        try
        {
            return JsonSerializer.Deserialize<List<AcceptanceCriterionDto>>(json, JsonOptions)
                   ?? (IReadOnlyList<AcceptanceCriterionDto>)Array.Empty<AcceptanceCriterionDto>();
        }
        catch (JsonException)
        {
            return Array.Empty<AcceptanceCriterionDto>();
        }
    }

    // Bullets, checkboxes and numbering, applied repeatedly so "- [ ] thing" reduces to "thing".
    [GeneratedRegex(@"^(?:[-*+\u2022]|\[\s*[xX]?\s*\]|\d+[.)])\s*(?:[-*+\u2022]|\[\s*[xX]?\s*\]|\d+[.)])?\s*")]
    private static partial Regex MarkerPrefix();
}
