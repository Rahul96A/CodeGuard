namespace CodeGuard.Core;

/// <summary>Stops tools reading outside the repository (e.g. "../../etc/passwd").</summary>
public static class PathGuard
{
    public static string Resolve(string root, string relativePath)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        if (!full.StartsWith(fullRoot, StringComparison.Ordinal))
            throw new UnauthorizedAccessException($"Path '{relativePath}' is outside the repository root.");
        return full;
    }
}
