using System.Text.RegularExpressions;

namespace CodeGuard.Core;

/// <summary>Minimal unified-diff parser: extracts added lines with their new-file line numbers.</summary>
public static partial class DiffParser
{
    [GeneratedRegex(@"^@@ -\d+(?:,\d+)? \+(\d+)(?:,\d+)? @@")]
    private static partial Regex HunkHeader();

    public static IReadOnlyList<AddedLine> ParseAddedLines(string diff)
    {
        var result = new List<AddedLine>();
        string? file = null;
        var inHeader = false;
        var line = 0;

        foreach (var raw in diff.Split('\n'))
        {
            var l = raw.TrimEnd('\r');

            if (l.StartsWith("diff --git ", StringComparison.Ordinal)) { inHeader = true; file = null; continue; }

            if (inHeader || file is null)
            {
                if (l.StartsWith("+++ ", StringComparison.Ordinal))
                {
                    var path = l[4..].Trim();
                    file = path == "/dev/null" ? null : path.StartsWith("b/", StringComparison.Ordinal) ? path[2..] : path;
                    continue;
                }
                if (l.StartsWith("--- ", StringComparison.Ordinal)) continue;
            }

            var hunk = HunkHeader().Match(l);
            if (hunk.Success) { inHeader = false; line = int.Parse(hunk.Groups[1].Value); continue; }

            if (inHeader || file is null) continue;

            if (l.StartsWith('+')) { result.Add(new AddedLine(file, line, l[1..])); line++; }
            else if (l.StartsWith('-') || l.StartsWith('\\')) { /* removed line or "\ No newline" */ }
            else line++; // context line
        }
        return result;
    }

    public static IReadOnlySet<string> ChangedFiles(string diff) =>
        ParseAddedLines(diff).Select(a => a.File).ToHashSet(StringComparer.OrdinalIgnoreCase);
}
