using System.Globalization;
using System.Text;

namespace NexaDock.Core.Launcher;

public enum LauncherItemKind
{
    App,
    File,
    Folder,
    Calculation,
    Conversion,
    Web,

    /// <summary>A clipboard history entry (Target holds its ID).</summary>
    Clip,
}

/// <summary>One line in the launcher's results.</summary>
/// <param name="Target">What runs on Enter: a shortcut target, a path or a URL.</param>
/// <param name="CopyText">For calculations and conversions: what Enter copies.</param>
public sealed record LauncherResult(
    string Title,
    string Subtitle,
    LauncherItemKind Kind,
    string? Target,
    double Score,
    string? CopyText = null);

/// <summary>An installed app as listed in Start (Win32 and Store apps alike).</summary>
/// <param name="Target">"shell:AppsFolder\…" or a path; launchable and usable for its icon.</param>
public sealed record AppEntry(string Name, string Target);

/// <summary>A file or folder found by Windows Search.</summary>
public sealed record FileHit(string Path, string Name, bool IsFolder);

public interface IAppIndex
{
    /// <summary>Installed apps (cached; refreshed in the background).</summary>
    Task<IReadOnlyList<AppEntry>> GetAppsAsync(CancellationToken cancellationToken);
}

public interface IFileSearch
{
    /// <summary>Files and folders whose name contains the text, from the Windows Search index.</summary>
    Task<IReadOnlyList<FileHit>> SearchAsync(string text, int max, CancellationToken cancellationToken);
}

/// <summary>
/// Scores how well a typed query matches a name, the way people type in a launcher:
/// "code" finds "Visual Studio Code", "vsc" its initials, "word" "Microsoft Word". Case and
/// diacritics are ignored ("cetvrtak" finds "Četvrtak"). Zero means no match.
/// </summary>
public static class FuzzyMatcher
{
    public static double Score(string query, string candidate)
    {
        var q = Normalize(query);
        var c = Normalize(candidate);
        if (q.Length == 0 || c.Length == 0)
        {
            return 0;
        }

        if (c == q)
        {
            return 1000;
        }

        if (c.StartsWith(q, StringComparison.Ordinal))
        {
            return 900 - Math.Min(50, c.Length - q.Length);
        }

        var words = Words(c);
        var queryWords = Words(q);

        // Every typed word starts some word of the name ("stu cod" → "Visual Studio Code").
        if (queryWords.Length > 1 && queryWords.All(qw => words.Any(w => w.StartsWith(qw, StringComparison.Ordinal))))
        {
            return 780;
        }

        for (var i = 0; i < words.Length; i++)
        {
            if (words[i].StartsWith(q, StringComparison.Ordinal))
            {
                return 800 - i * 10;
            }
        }

        // Initials: "vsc" → Visual Studio Code, "gc" → Google Chrome.
        if (q.Length >= 2 && words.Length >= q.Length)
        {
            var initials = new string(words.Select(w => w[0]).ToArray());
            if (initials.StartsWith(q, StringComparison.Ordinal))
            {
                return 700;
            }
        }

        var index = c.IndexOf(q, StringComparison.Ordinal);
        if (index >= 0)
        {
            return 500 - Math.Min(100, index);
        }

        // All letters in order ("chrme" → "chrome"), penalised by the gaps.
        if (q.Length >= 3)
        {
            var gaps = 0;
            var at = -1;
            foreach (var ch in q)
            {
                var next = c.IndexOf(ch, at + 1);
                if (next < 0)
                {
                    return 0;
                }

                if (at >= 0)
                {
                    gaps += next - at - 1;
                }

                at = next;
            }

            return Math.Max(1, 300 - gaps * 15);
        }

        return 0;
    }

    public static string Normalize(string text)
    {
        var decomposed = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch == 'đ' ? 'd' : ch);
            }
        }

        return sb.ToString();
    }

    private static string[] Words(string text) =>
        text.Split((char[])[' ', '-', '_', '.', '(', ')', '–', '/'], StringSplitOptions.RemoveEmptyEntries);
}

/// <summary>Builds the instant part of the results (no I/O): calculation, conversion, apps, web.</summary>
public static class LauncherEngine
{
    public const int MaxApps = 7;

    /// <param name="usage">How often each target was launched from here; frequent ones rank higher.</param>
    public static IReadOnlyList<LauncherResult> Query(
        string text,
        IReadOnlyList<AppEntry> apps,
        IReadOnlyDictionary<string, int> usage,
        CultureInfo culture)
    {
        var query = text.Trim();
        var results = new List<LauncherResult>();
        if (query.Length == 0)
        {
            return results;
        }

        if (Calculator.TryEvaluate(query, out var value))
        {
            var shown = Calculator.Format(value, culture);
            results.Add(new LauncherResult(shown, "= " + query.TrimStart('='), LauncherItemKind.Calculation, null, 2000, shown));
        }

        if (UnitConverter.TryConvert(query, culture, out var converted, out var explained))
        {
            results.Add(new LauncherResult(converted, explained, LauncherItemKind.Conversion, null, 1900, converted));
        }

        var appHits = apps
            .Select(a => (App: a, Score: FuzzyMatcher.Score(query, a.Name)))
            .Where(x => x.Score > 0)
            .Select(x => (x.App, Score: x.Score + Math.Min(20, usage.GetValueOrDefault(x.App.Target)) * 15))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.App.Name.Length)
            .DistinctBy(x => x.App.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaxApps);
        results.AddRange(appHits.Select(x => new LauncherResult(x.App.Name, string.Empty, LauncherItemKind.App, x.App.Target, x.Score)));

        results.Add(new LauncherResult(query, string.Empty, LauncherItemKind.Web,
            "https://www.google.com/search?q=" + Uri.EscapeDataString(query), 1));
        return results.OrderByDescending(r => r.Score).ToList();
    }
}
