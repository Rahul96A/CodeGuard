using System.Diagnostics;
using System.Text;

namespace CodeGuard.McpServer;

internal static class ProcessRunner
{
    /// <summary>Runs a process with an argument list (no shell, so no command injection) and a timeout.</summary>
    public static async Task<string> RunAsync(string fileName, IEnumerable<string> args, string workingDir, TimeSpan timeout, int maxChars = 20_000)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {fileName}");
        var output = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cts = new CancellationTokenSource(timeout);
        try { await process.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            return $"[timed out after {timeout.TotalSeconds:0}s]\n" + Tail(output.ToString(), maxChars);
        }
        return $"[exit code {process.ExitCode}]\n" + Tail(output.ToString(), maxChars);
    }

    private static string Tail(string s, int max) => s.Length <= max ? s : "...[truncated]...\n" + s[^max..];
}
