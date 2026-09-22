using System.Diagnostics;
using CodeGuard.Core;
using Microsoft.Extensions.AI;

namespace CodeGuard.Agent;

public sealed record ReviewRun(ReviewResult Result, TimeSpan Duration, long? InputTokens, long? OutputTokens, int ToolCalls);

/// <summary>
/// The agent loop: deterministic rules first, then the LLM with MCP tools, then grounding filters.
/// Pass a null <paramref name="chat"/> to run the deterministic rules only (no LLM needed).
/// </summary>
public sealed class Reviewer(IChatClient? chat, IList<AITool> tools, string rules, int maxDiffChars = 60_000)
{
    public const string RulesOnlySummary = "Deterministic rules only; no LLM provider was used for this review.";

    public async Task<ReviewRun> ReviewAsync(string diff, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        // 1. Cheap, reliable rules.
        var added = DiffParser.ParseAddedLines(diff);
        var ruleFindings = SecretScanner.Scan(added);

        if (chat is null)
        {
            var ordered = ruleFindings.OrderByDescending(f => f.Severity).ThenBy(f => f.File).ThenBy(f => f.Line).ToList();
            return new ReviewRun(new ReviewResult(RulesOnlySummary, ordered), sw.Elapsed, null, null, 0);
        }

        // 2. LLM + tools. FunctionInvokingChatClient runs the tool-call loop for us.
        var truncated = diff.Length > maxDiffChars;
        List<ChatMessage> messages =
        [
            new(ChatRole.System, Prompts.System(rules)),
            new(ChatRole.User, Prompts.User(truncated ? diff[..maxDiffChars] : diff, truncated, ruleFindings)),
        ];
        var response = await chat.GetResponseAsync(messages, new ChatOptions { Tools = tools, Temperature = 0.1f }, ct);

        var toolCalls = response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().Count();
        var llm = ReviewJson.TryParse(response.Text);
        if (llm is null)
        {
            // Keep the raw text out of the report (it may echo the untrusted diff), but let developers see it.
            if (string.Equals(Environment.GetEnvironmentVariable("CODEGUARD_DEBUG"), "true", StringComparison.OrdinalIgnoreCase))
                Console.Error.WriteLine($"[debug] unparseable model reply:{Environment.NewLine}{response.Text}");
            llm = new ReviewResult("The model reply could not be parsed as JSON. Raw reply was ignored.", []);
        }

        // 3. Grounding: drop findings on files that are not in the diff (a common hallucination).
        var changed = added.Select(a => a.File).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var llmFindings = llm.Findings.Where(f => changed.Contains(f.File.TrimStart('/'))).Select(f => f with { Source = "llm" });

        // 4. Merge + de-duplicate (rule findings win).
        var merged = ruleFindings.Concat(llmFindings)
            .GroupBy(f => (f.File.ToLowerInvariant(), f.Line, f.RuleId))
            .Select(g => g.First())
            .OrderByDescending(f => f.Severity).ThenBy(f => f.File).ThenBy(f => f.Line)
            .ToList();

        return new ReviewRun(new ReviewResult(llm.Summary, merged), sw.Elapsed,
            response.Usage?.InputTokenCount, response.Usage?.OutputTokenCount, toolCalls);
    }
}
