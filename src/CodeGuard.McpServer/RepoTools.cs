using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using CodeGuard.Core;
using ModelContextProtocol.Server;

namespace CodeGuard.McpServer;

[McpServerToolType]
public sealed partial class RepoTools(RepoContext ctx)
{
    private static readonly string[] IgnoredDirs = ["bin", "obj", ".git", "node_modules", ".vs"];

    [McpServerTool(Name = "list_changed_files", ReadOnly = true)]
    [Description("Lists files changed between a base git ref and HEAD, with status (A=added, M=modified, D=deleted).")]
    public Task<string> ListChangedFiles([Description("Base git ref, for example origin/main")] string baseRef = "origin/main")
        => ProcessRunner.RunAsync("git", ["diff", "--name-status", $"{SafeRef(baseRef)}...HEAD"], ctx.Root, TimeSpan.FromSeconds(30));

    [McpServerTool(Name = "get_file_diff", ReadOnly = true)]
    [Description("Returns the unified diff for one file between a base git ref and HEAD.")]
    public Task<string> GetFileDiff(
        [Description("Repository-relative file path")] string path,
        [Description("Base git ref, for example origin/main")] string baseRef = "origin/main")
    {
        PathGuard.Resolve(ctx.Root, path);
        return ProcessRunner.RunAsync("git", ["diff", "--unified=5", $"{SafeRef(baseRef)}...HEAD", "--", path], ctx.Root, TimeSpan.FromSeconds(30));
    }

    [McpServerTool(Name = "read_file", ReadOnly = true)]
    [Description("Reads a repository file with line numbers. Use it to see context around a changed line.")]
    public async Task<string> ReadFile(
        [Description("Repository-relative file path")] string path,
        [Description("First line to return (1-based)")] int startLine = 1,
        [Description("Maximum number of lines to return (max 400)")] int maxLines = 200)
    {
        var full = PathGuard.Resolve(ctx.Root, path);
        if (!File.Exists(full)) return $"File not found: {path}";
        var lines = await File.ReadAllLinesAsync(full);
        startLine = Math.Max(1, startLine);
        maxLines = Math.Clamp(maxLines, 1, 400);
        var sb = new StringBuilder();
        for (var i = startLine - 1; i < Math.Min(lines.Length, startLine - 1 + maxLines); i++)
            sb.Append(i + 1).Append(": ").AppendLine(lines[i]);
        return sb.Length == 0 ? "(no lines in range)" : sb.ToString();
    }

    [McpServerTool(Name = "search_code", ReadOnly = true)]
    [Description("Regex search across repository files. Use it to check how a method is used or whether auth/validation exists elsewhere.")]
    public string SearchCode(
        [Description(".NET regular expression")] string pattern,
        [Description("File glob, for example *.cs or *.json")] string fileGlob = "*.cs",
        [Description("Maximum matches to return (max 100)")] int maxResults = 50)
    {
        Regex regex;
        try { regex = new Regex(pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250)); } // timeout guards against ReDoS
        catch (ArgumentException ex) { return $"Invalid regex: {ex.Message}"; }

        maxResults = Math.Clamp(maxResults, 1, 100);
        var sb = new StringBuilder();
        var count = 0;
        foreach (var file in EnumerateFiles(fileGlob))
        {
            var rel = Path.GetRelativePath(ctx.Root, file).Replace('\\', '/');
            var lineNo = 0;
            foreach (var line in File.ReadLines(file))
            {
                lineNo++;
                bool match;
                try { match = regex.IsMatch(line); } catch (RegexMatchTimeoutException) { return "Regex timed out; simplify the pattern."; }
                if (!match) continue;
                sb.Append(rel).Append(':').Append(lineNo).Append(": ").AppendLine(line.Trim());
                if (++count >= maxResults) return sb.AppendLine("...[max results reached]").ToString();
            }
        }
        return count == 0 ? "No matches." : sb.ToString();
    }

    [McpServerTool(Name = "scan_secrets", ReadOnly = true)]
    [Description("Runs CodeGuard's deterministic secret and TFN scanner over a whole file.")]
    public async Task<string> ScanSecrets([Description("Repository-relative file path")] string path)
    {
        var full = PathGuard.Resolve(ctx.Root, path);
        if (!File.Exists(full)) return $"File not found: {path}";
        var lines = (await File.ReadAllLinesAsync(full)).Select((t, i) => new AddedLine(path, i + 1, t));
        var findings = SecretScanner.Scan(lines);
        return findings.Count == 0
            ? "No secrets detected."
            : string.Join('\n', findings.Select(f => $"{f.File}:{f.Line} [{f.RuleId}/{f.Severity}] {f.Title}"));
    }

    [McpServerTool(Name = "run_dotnet_build", Destructive = false)]
    [Description("Runs 'dotnet build' in the repository and returns the tail of the output. Disabled unless CODEGUARD_ALLOW_EXEC=true.")]
    public Task<string> RunDotnetBuild() => Exec(["build", "--nologo", "-v", "q"]);

    [McpServerTool(Name = "run_dotnet_test", Destructive = false)]
    [Description("Runs 'dotnet test' in the repository and returns the tail of the output. Disabled unless CODEGUARD_ALLOW_EXEC=true.")]
    public Task<string> RunDotnetTest() => Exec(["test", "--nologo", "-v", "q"]);

    private Task<string> Exec(string[] args) => ctx.AllowExec
        ? ProcessRunner.RunAsync("dotnet", args, ctx.Root, TimeSpan.FromMinutes(5), maxChars: 6_000)
        : Task.FromResult("Execution tools are disabled. Set CODEGUARD_ALLOW_EXEC=true only for trusted code.");

    private IEnumerable<string> EnumerateFiles(string glob) =>
        Directory.EnumerateFiles(ctx.Root, glob, SearchOption.AllDirectories)
            .Where(f => !Path.GetRelativePath(ctx.Root, f).Split(Path.DirectorySeparatorChar).Any(IgnoredDirs.Contains));

    /// <summary>Rejects refs like "--output=/tmp/x" that git would treat as options (argument injection).</summary>
    private static string SafeRef(string gitRef) =>
        GitRef().IsMatch(gitRef) && !gitRef.StartsWith('-')
            ? gitRef
            : throw new ArgumentException($"Invalid git ref: {gitRef}");

    [GeneratedRegex(@"^[A-Za-z0-9._/\-]{1,100}$")]
    private static partial Regex GitRef();
}
