using System.Text;
using System.Text.Json;
using CodeGuard.Core;
using Microsoft.Extensions.AI;

namespace CodeGuard.Agent;

public sealed record ExpectedFinding(string RuleId, string File, int Line);

/// <summary>
/// Runs the reviewer over seeded cases and computes precision / recall.
/// A predicted finding matches an expected one when rule ID and file match and the line is within ±3.
/// </summary>
public static class EvalRunner
{
    public static async Task<int> RunAsync(string casesDir, IChatClient? chat, string rules, string provider, string outFile, CancellationToken ct)
    {
        int tp = 0, fp = 0, fn = 0;
        var report = new StringBuilder($"# CodeGuard eval results\n\nProvider: {provider} · {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC\n\n");
        report.AppendLine("| Case | Expected | Predicted | TP | FP | FN | Seconds |").AppendLine("|---|---|---|---|---|---|---|");

        foreach (var caseDir in Directory.GetDirectories(casesDir).Order())
        {
            var name = Path.GetFileName(caseDir);
            var diff = await File.ReadAllTextAsync(Path.Combine(caseDir, "diff.patch"), ct);
            var expected = JsonSerializer.Deserialize<List<ExpectedFinding>>(
                await File.ReadAllTextAsync(Path.Combine(caseDir, "expected.json"), ct), Json.Options) ?? [];

            // Each case gets its own MCP server rooted at the case's post-change files.
            await using var mcp = await McpConnection.ConnectAsync(Path.Combine(caseDir, "files"), ct);
            var tools = (await mcp.ListToolsAsync(cancellationToken: ct)).Cast<AITool>().ToList();
            var run = await new Reviewer(chat, tools, rules).ReviewAsync(diff, ct);
            var predicted = run.Result.Findings;

            var unmatched = predicted.ToList();
            int cTp = 0;
            foreach (var e in expected)
            {
                var hit = unmatched.FirstOrDefault(p =>
                    p.RuleId.Equals(e.RuleId, StringComparison.OrdinalIgnoreCase) &&
                    p.File.Equals(e.File, StringComparison.OrdinalIgnoreCase) &&
                    Math.Abs(p.Line - e.Line) <= 3);
                if (hit is not null) { cTp++; unmatched.Remove(hit); }
            }
            int cFn = expected.Count - cTp, cFp = unmatched.Count;
            tp += cTp; fp += cFp; fn += cFn;

            Console.WriteLine($"  {name,-24} TP={cTp} FP={cFp} FN={cFn}");
            report.AppendLine($"| {name} | {expected.Count} | {predicted.Count} | {cTp} | {cFp} | {cFn} | {run.Duration.TotalSeconds:0.0} |");
        }

        double precision = tp + fp == 0 ? 1 : (double)tp / (tp + fp);
        double recall = tp + fn == 0 ? 1 : (double)tp / (tp + fn);
        report.AppendLine().AppendLine($"**Precision:** {precision:P0} · **Recall:** {recall:P0} · TP={tp} FP={fp} FN={fn}");
        await File.WriteAllTextAsync(outFile, report.ToString(), ct);

        Console.WriteLine($"\nPrecision {precision:P0}  Recall {recall:P0}  → {outFile}");
        return 0;
    }
}
