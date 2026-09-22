using System.Text;
using CodeGuard.Core;

namespace CodeGuard.Agent;

public static class ReportRenderer
{
    public const string Marker = "<!-- codeguard-review -->";

    public static string ToMarkdown(ReviewRun run, string provider)
    {
        var r = run.Result;
        var sb = new StringBuilder(Marker).AppendLine();
        sb.AppendLine("## 🛡️ CodeGuard review").AppendLine();
        sb.AppendLine(r.Summary).AppendLine();

        if (r.Findings.Count == 0)
        {
            sb.AppendLine("✅ No issues found.");
        }
        else
        {
            sb.AppendLine("| Severity | Rule | Location | Issue |");
            sb.AppendLine("|---|---|---|---|");
            foreach (var f in r.Findings)
                sb.AppendLine($"| {Icon(f.Severity)} {f.Severity} | `{f.RuleId}` | `{f.File}:{f.Line}` | **{Esc(f.Title)}** — {Esc(f.Explanation)} |");

            sb.AppendLine().AppendLine("<details><summary>Suggested fixes</summary>").AppendLine();
            foreach (var f in r.Findings.Where(f => !string.IsNullOrWhiteSpace(f.Suggestion)))
                sb.AppendLine($"- `{f.File}:{f.Line}` ({f.RuleId}): {f.Suggestion}");
            sb.AppendLine().AppendLine("</details>");
        }

        sb.AppendLine().Append($"<sub>{provider} · {run.Duration.TotalSeconds:0.0}s · tool calls: {run.ToolCalls}");
        if (run.InputTokens is not null) sb.Append($" · tokens in/out: {run.InputTokens}/{run.OutputTokens}");
        sb.AppendLine(r.Summary == Reviewer.RulesOnlySummary
            ? " · deterministic rules only, no LLM.</sub>"
            : " · AI-generated, verify before acting.</sub>");
        return sb.ToString();
    }

    private static string Icon(Severity s) => s switch
    {
        Severity.Critical => "🔴", Severity.High => "🟠", Severity.Medium => "🟡", Severity.Low => "🔵", _ => "⚪",
    };

    private static string Esc(string s) => s.Replace("|", "\\|").ReplaceLineEndings(" ");
}
