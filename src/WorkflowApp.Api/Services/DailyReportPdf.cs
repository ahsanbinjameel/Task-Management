using System.Globalization;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using WorkflowApp.Application.Reporting;

namespace WorkflowApp.Api.Services;

public interface IDailyReportPdf
{
    byte[] Team(DailyTeamReportDto report);
    byte[] Person(DailyUserReportDto report);
}

/// <summary>
/// The daily report as a document somebody can hand to a manager.
///
/// The CSV is for a spreadsheet and is deliberately one flat row per person, which is exactly what
/// makes it the wrong shape for reading: a day's quick work is a variable number of lines, and a
/// row cannot hold that. So this is a second rendering rather than a conversion of the first, and
/// it is built from the same <see cref="DailyUserReportDto"/> the screen shows — one set of
/// figures, three presentations, no third place for them to drift.
///
/// It lives in the API layer on purpose. PDF is a transport format, the same as CSV or JSON; the
/// Application layer knows what the numbers mean and should not know how they are drawn.
///
/// <para>
/// MigraDoc rather than hand-built PDF because the requirements include page numbers and tables
/// that break across pages, and both are the sort of thing that looks easy until the third page.
/// It is MIT-licensed, so there is no revenue condition to keep track of.
/// </para>
/// </summary>
public sealed class DailyReportPdf : IDailyReportPdf
{
    private const string BodyFont = "Segoe UI";

    /// <summary>
    /// PDFsharp resolves fonts through a process-wide hook, not through DI. Program.cs sets it at
    /// startup; this is the safety net for anything that constructs the renderer directly — a
    /// smoke run, a test — so the failure is never "no appropriate font found" three frames deep.
    /// </summary>
    static DailyReportPdf()
    {
        if (PdfSharp.Fonts.GlobalFontSettings.FontResolver is not null) return;

        var resolver = new FileSystemFontResolver();
        resolver.EnsureAvailable();
        PdfSharp.Fonts.GlobalFontSettings.FontResolver = resolver;
    }

    public byte[] Team(DailyTeamReportDto report)
    {
        var document = NewDocument($"Daily team report — {report.Date:d MMMM yyyy}");
        var section = document.LastSection;

        Heading(section, "Daily team report");
        Subheading(section, report.Date.ToString("dddd, d MMMM yyyy", CultureInfo.InvariantCulture));

        SummaryLine(section, new (string, string)[]
        {
            ("People on shift", report.PeopleOnShift.ToString(CultureInfo.InvariantCulture)),
            ("Shift time", Hours(report.TotalShiftTime)),
            ("Productive", Hours(report.TotalProductiveTime)),
            ("Tasks completed", report.TasksCompleted.ToString(CultureInfo.InvariantCulture)),
        });

        if (report.Users.Count == 0)
        {
            var empty = section.AddParagraph("Nobody was on shift on this day.");
            empty.Format.Font.Color = Colors.Gray;
            return Render(document);
        }

        // The at-a-glance table first, then a page per person. A manager reading this wants the
        // shape of the day before the detail, and someone querying one line wants the detail
        // without hunting for it.
        var table = NewTable(section, 6);
        HeaderRow(table, "Person", "Shift", "Productive", "Away", "Tasks", "Quick work");

        foreach (var user in report.Users)
        {
            BodyRow(table,
                user.DisplayName,
                Hours(user.ShiftDuration),
                Hours(user.ProductiveTime),
                Hours(user.BreakTime),
                user.TasksWorked.ToString(CultureInfo.InvariantCulture),
                Hours(user.QuickWorkTime));
        }

        foreach (var user in report.Users)
        {
            section.AddPageBreak();
            PersonBody(section, user, includeHeading: true);
        }

        return Render(document);
    }

    public byte[] Person(DailyUserReportDto report)
    {
        var document = NewDocument($"{report.DisplayName} — {report.Date:d MMMM yyyy}");
        var section = document.LastSection;

        Heading(section, report.DisplayName);
        Subheading(section, report.Date.ToString("dddd, d MMMM yyyy", CultureInfo.InvariantCulture));

        PersonBody(section, report, includeHeading: false);
        return Render(document);
    }

    // --- the body of one person's day -----------------------------------------------------

    private static void PersonBody(Section section, DailyUserReportDto user, bool includeHeading)
    {
        if (includeHeading)
        {
            var name = section.AddParagraph(user.DisplayName);
            name.Format.Font.Size = 15;
            name.Format.Font.Bold = true;
            name.Format.SpaceAfter = Unit.FromPoint(2);
        }

        var times = user.ShiftStart is { } start
            ? $"On shift {start.ToLocalTime():HH:mm}–"
              + (user.ShiftEnd is { } end ? $"{end.ToLocalTime():HH:mm}" : "still on")
            : "Not on shift";

        var line = section.AddParagraph(times);
        line.Format.Font.Color = Colors.Gray;
        line.Format.SpaceAfter = Unit.FromPoint(10);

        SummaryLine(section, new (string, string)[]
        {
            ("Shift", Hours(user.ShiftDuration)),
            ("Productive", Hours(user.ProductiveTime)),
            ("Away", Hours(user.BreakTime)),
            ("Quick work", Hours(user.QuickWorkTime)),
            ("Interruptions", user.Interruptions.ToString(CultureInfo.InvariantCulture)),
            ("Completed", user.TasksCompleted.ToString(CultureInfo.InvariantCulture)),
        });

        // --- work detail ---------------------------------------------------------------------
        SectionTitle(section, "Work they are responsible for");

        if (user.OwnedWork.Count == 0)
        {
            Note(section, "No time logged on their own tasks.");
        }
        else
        {
            var table = NewTable(section, 4);
            HeaderRow(table, "Task", "Title", "Sittings", "Time");

            foreach (var line2 in user.OwnedWork)
            {
                BodyRow(table, line2.TaskNumber, line2.Title,
                    line2.Sessions.ToString(CultureInfo.InvariantCulture), Hours(line2.TimeSpent));
            }
        }

        // Kept apart from the figures above, because helping with a task is not the same as being
        // accountable for it — the whole point of the Support Person distinction.
        if (user.SupportWork.Count > 0)
        {
            SectionTitle(section, "Work they helped with");
            Note(section, "Somebody else is responsible for these. Not counted as their work.");

            var table = NewTable(section, 4);
            HeaderRow(table, "Task", "Title", "Sittings", "Time");

            foreach (var line3 in user.SupportWork)
            {
                BodyRow(table, line3.TaskNumber, line3.Title,
                    line3.Sessions.ToString(CultureInfo.InvariantCulture), Hours(line3.TimeSpent));
            }
        }

        // --- quick work ----------------------------------------------------------------------
        SectionTitle(section, "Work that arrived without a request");

        if (user.QuickWork.Count == 0)
        {
            Note(section, "No phone calls or walk-ups recorded.");
        }
        else
        {
            var table = NewTable(section, 5);
            HeaderRow(table, "Started", "What", "Outcome", "Interrupted", "Time");

            foreach (var quick in user.QuickWork)
            {
                var row = BodyRow(table,
                    quick.StartedAt.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture),
                    quick.Title + (quick.ClientName is { } c ? $" ({c})" : string.Empty),
                    quick.Outcome ?? string.Empty,
                    quick.InterruptedTaskNumber ?? string.Empty,
                    // A cancelled record is shown and not totalled — see the report service. Saying
                    // so on the page stops a reader adding it up themselves and disagreeing with
                    // the summary above.
                    quick.WasCancelled ? "not counted" : Hours(quick.Duration));

                if (quick.WasCancelled)
                {
                    for (var i = 0; i < 5; i++) row.Cells[i].Format.Font.Color = Colors.Gray;
                }
            }
        }

        // --- notes ---------------------------------------------------------------------------
        // The outcomes in full. The table above truncates nothing, but a long outcome in a narrow
        // column is unreadable, and these are the sentences the report actually exists to carry.
        var outcomes = user.QuickWork
            .Where(q => !q.WasCancelled && !string.IsNullOrWhiteSpace(q.Outcome))
            .ToList();

        if (outcomes.Count > 0)
        {
            SectionTitle(section, "Notes");

            foreach (var quick in outcomes)
            {
                var note = section.AddParagraph();
                note.AddFormattedText(
                    quick.StartedAt.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture)
                    + "  " + quick.Title + " — ",
                    TextFormat.Bold);
                note.AddText(quick.Outcome!);
                note.Format.SpaceAfter = Unit.FromPoint(5);
                note.Format.LeftIndent = Unit.FromPoint(8);
            }
        }
    }

    // --- document furniture ---------------------------------------------------------------

    private static Document NewDocument(string title)
    {
        var document = new Document { Info = { Title = title, Author = "WorkflowApp" } };

        var normal = document.Styles["Normal"]!;
        normal.Font.Name = BodyFont;
        normal.Font.Size = 9.5;

        var section = document.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.TopMargin = Unit.FromCentimeter(1.8);
        section.PageSetup.BottomMargin = Unit.FromCentimeter(1.8);
        section.PageSetup.LeftMargin = Unit.FromCentimeter(1.6);
        section.PageSetup.RightMargin = Unit.FromCentimeter(1.6);

        // "Page 2 of 7" on every page. Printed reports get separated, and a page with no number is
        // a page nobody can put back.
        var footer = section.Footers.Primary.AddParagraph();
        footer.AddText("Page ");
        footer.AddPageField();
        footer.AddText(" of ");
        footer.AddNumPagesField();
        footer.Format.Alignment = ParagraphAlignment.Right;
        footer.Format.Font.Size = 8;
        footer.Format.Font.Color = Colors.Gray;

        return document;
    }

    private static void Heading(Section section, string text)
    {
        var heading = section.AddParagraph(text);
        heading.Format.Font.Size = 18;
        heading.Format.Font.Bold = true;
        heading.Format.SpaceAfter = Unit.FromPoint(1);
    }

    private static void Subheading(Section section, string text)
    {
        var sub = section.AddParagraph(text);
        sub.Format.Font.Size = 10;
        sub.Format.Font.Color = Colors.Gray;
        sub.Format.SpaceAfter = Unit.FromPoint(14);
    }

    private static void SectionTitle(Section section, string text)
    {
        var title = section.AddParagraph(text);
        title.Format.Font.Size = 11;
        title.Format.Font.Bold = true;
        title.Format.SpaceBefore = Unit.FromPoint(14);
        title.Format.SpaceAfter = Unit.FromPoint(5);
    }

    private static void Note(Section section, string text)
    {
        var note = section.AddParagraph(text);
        note.Format.Font.Color = Colors.Gray;
        note.Format.SpaceAfter = Unit.FromPoint(6);
    }

    /// <summary>The summary strip: label above value, laid out as a borderless single-row table.</summary>
    private static void SummaryLine(Section section, IReadOnlyList<(string Label, string Value)> cells)
    {
        var table = section.AddTable();
        table.Borders.Width = 0;

        for (var i = 0; i < cells.Count; i++)
            table.AddColumn(Unit.FromCentimeter(17.8 / cells.Count));

        var labels = table.AddRow();
        var values = table.AddRow();

        for (var i = 0; i < cells.Count; i++)
        {
            var label = labels.Cells[i].AddParagraph(cells[i].Label.ToUpperInvariant());
            label.Format.Font.Size = 7.5;
            label.Format.Font.Color = Colors.Gray;

            var value = values.Cells[i].AddParagraph(cells[i].Value);
            value.Format.Font.Size = 13;
            value.Format.Font.Bold = true;
        }

        values.Format.SpaceAfter = Unit.FromPoint(8);
    }

    private static Table NewTable(Section section, int columns)
    {
        var table = section.AddTable();
        table.Borders.Width = 0;
        table.Borders.Bottom.Width = 0.4;
        table.Borders.Bottom.Color = Color.FromRgb(220, 220, 220);

        // The title column takes the slack; the rest are sized to their content. A table of equal
        // columns wastes half the page on "2h 10m" and truncates the thing you wanted to read.
        var widths = columns switch
        {
            4 => new[] { 2.6, 10.4, 1.9, 2.9 },
            5 => new[] { 1.8, 5.4, 6.2, 2.4, 2.0 },
            _ => new[] { 4.4, 2.7, 2.7, 2.4, 1.9, 3.7 },
        };

        for (var i = 0; i < columns; i++)
            table.AddColumn(Unit.FromCentimeter(widths[i]));

        return table;
    }

    private static void HeaderRow(Table table, params string[] headings)
    {
        var row = table.AddRow();
        row.HeadingFormat = true;   // repeats the header when the table breaks across a page
        row.Format.Font.Bold = true;
        row.Format.Font.Size = 8;
        row.Shading.Color = Color.FromRgb(244, 245, 247);

        for (var i = 0; i < headings.Length; i++)
        {
            var cell = row.Cells[i].AddParagraph(headings[i].ToUpperInvariant());
            cell.Format.Font.Color = Colors.Gray;
        }

        row.TopPadding = Unit.FromPoint(4);
        row.BottomPadding = Unit.FromPoint(4);
    }

    private static Row BodyRow(Table table, params string[] values)
    {
        var row = table.AddRow();
        row.TopPadding = Unit.FromPoint(3.5);
        row.BottomPadding = Unit.FromPoint(3.5);
        row.KeepWith = 0;

        for (var i = 0; i < values.Length; i++)
            row.Cells[i].AddParagraph(values[i]);

        return row;
    }

    private static string Hours(TimeSpan value)
    {
        if (value <= TimeSpan.Zero) return "—";

        var minutes = (int)Math.Round(value.TotalMinutes);
        return minutes < 60
            ? $"{minutes}m"
            : minutes % 60 == 0 ? $"{minutes / 60}h" : $"{minutes / 60}h {minutes % 60}m";
    }

    private static byte[] Render(Document document)
    {
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();

        using var stream = new MemoryStream();
        renderer.PdfDocument.Save(stream, false);
        return stream.ToArray();
    }
}
