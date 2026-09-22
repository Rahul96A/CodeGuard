namespace CodeGuard.Core;

public static class RulesLoader
{
    /// <summary>Loads every *.md file in the rules folder; these become part of the system prompt.</summary>
    public static string Load(string rulesDirectory)
    {
        if (!Directory.Exists(rulesDirectory)) return "";
        return string.Join("\n\n", Directory.GetFiles(rulesDirectory, "*.md").Order().Select(File.ReadAllText));
    }

    /// <summary>Walks up from a start folder to find the repo's "rules" folder.</summary>
    public static string? FindRulesDirectory(string start)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "rules");
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }
}
