using System.Collections.Concurrent;
using PdfSharp.Fonts;

namespace WorkflowApp.Api.Services;

/// <summary>
/// Where PDFsharp gets its fonts.
///
/// PDFsharp 6's cross-platform build ships no font handling at all — it will not draw a character
/// until something tells it where the glyphs are. That is a deliberate choice on their part, and it
/// means this class is not optional plumbing: without it every report throws at render time, not at
/// startup, which is the worst place to find out.
///
/// It reads TrueType files off the machine, trying a short list of families in order and taking the
/// first that is actually installed. That covers a Windows Server (Segoe UI, Arial) and a Linux
/// container (DejaVu, Liberation) without either of them needing anything done to it, and without
/// committing the repository to redistributing a font.
///
/// <para>
/// Two rules keep it from failing quietly. Every unknown family falls back to the resolved default
/// rather than returning null — MigraDoc asks for "Courier New" for its own internal error font,
/// and a null there turns a missing italic into an unhandled exception. And if <i>nothing</i>
/// resolves, <see cref="EnsureAvailable"/> says so at startup with the list it looked for, rather
/// than letting the first person to open a report find out.
/// </para>
/// </summary>
public sealed class FileSystemFontResolver : IFontResolver
{
    /// <summary>
    /// Candidate families, best first. Each entry names the four files that make up a family;
    /// a missing bold or italic falls back to the regular face rather than disqualifying it.
    /// </summary>
    private static readonly (string Family, string Regular, string Bold, string Italic, string BoldItalic)[] Candidates =
    {
        ("Segoe UI", "segoeui.ttf", "segoeuib.ttf", "segoeuii.ttf", "segoeuiz.ttf"),
        ("Arial", "arial.ttf", "arialbd.ttf", "ariali.ttf", "arialbi.ttf"),
        ("DejaVu Sans", "DejaVuSans.ttf", "DejaVuSans-Bold.ttf", "DejaVuSans-Oblique.ttf", "DejaVuSans-BoldOblique.ttf"),
        ("Liberation Sans", "LiberationSans-Regular.ttf", "LiberationSans-Bold.ttf", "LiberationSans-Italic.ttf", "LiberationSans-BoldItalic.ttf"),
    };

    /// <summary>Everywhere a system font might live, across the platforms this could run on.</summary>
    private static readonly string[] SearchRoots =
    {
        Environment.GetFolderPath(Environment.SpecialFolder.Fonts),
        "/usr/share/fonts",
        "/usr/local/share/fonts",
        "/Library/Fonts",
        "/System/Library/Fonts",
    };

    private static readonly ConcurrentDictionary<string, byte[]> Cache = new();

    private readonly Lazy<Resolved?> _resolved = new(FindFirstAvailable, isThreadSafe: true);

    private sealed record Resolved(string Family, string Regular, string Bold, string Italic, string BoldItalic);

    /// <summary>
    /// Called once at startup so a machine with no usable font fails there, loudly, instead of on
    /// the first report somebody tries to print.
    /// </summary>
    public void EnsureAvailable()
    {
        if (_resolved.Value is not null) return;

        throw new InvalidOperationException(
            "No usable font was found for PDF rendering. Looked for "
            + string.Join(", ", Candidates.Select(c => c.Family))
            + " under " + string.Join(", ", SearchRoots.Where(r => !string.IsNullOrEmpty(r)))
            + ". Install one of them, or add its .ttf to the fonts directory.");
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        var resolved = _resolved.Value;
        if (resolved is null) return null;

        // Every family maps onto the one we found, including families we were never asked to
        // support. Returning null for an unrecognised name is what turns a cosmetic mismatch into
        // an unhandled exception three frames inside MigraDoc.
        var face = (isBold, isItalic) switch
        {
            (true, true) => resolved.BoldItalic,
            (true, false) => resolved.Bold,
            (false, true) => resolved.Italic,
            _ => resolved.Regular,
        };

        return new FontResolverInfo(face);
    }

    public byte[]? GetFont(string faceName) =>
        Cache.GetOrAdd(faceName, path => File.Exists(path) ? File.ReadAllBytes(path) : Array.Empty<byte>())
            is { Length: > 0 } bytes
            ? bytes
            : null;

    private static Resolved? FindFirstAvailable()
    {
        foreach (var candidate in Candidates)
        {
            var regular = Locate(candidate.Regular);
            if (regular is null) continue;

            // A family missing its italic is still a usable family — falling back to the regular
            // face costs a little emphasis, where refusing it costs the whole report.
            return new Resolved(
                candidate.Family,
                regular,
                Locate(candidate.Bold) ?? regular,
                Locate(candidate.Italic) ?? regular,
                Locate(candidate.BoldItalic) ?? Locate(candidate.Bold) ?? regular);
        }

        return null;
    }

    private static string? Locate(string fileName)
    {
        foreach (var root in SearchRoots)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;

            var direct = Path.Combine(root, fileName);
            if (File.Exists(direct)) return direct;

            // Linux nests fonts a couple of levels down (`/usr/share/fonts/truetype/dejavu/...`).
            try
            {
                var found = Directory
                    .EnumerateFiles(root, fileName, SearchOption.AllDirectories)
                    .FirstOrDefault();

                if (found is not null) return found;
            }
            catch (UnauthorizedAccessException)
            {
                // A font directory we cannot read is not an error; try the next root.
            }
            catch (DirectoryNotFoundException)
            {
            }
        }

        return null;
    }
}
