using CodeGuard.Agent;
using CodeGuard.Core;
using Microsoft.Extensions.AI;

namespace CodeGuard.Core.Tests;

/// <summary>A fake LLM: returns a canned reply so the agent logic can be tested for free and deterministically.</summary>
file sealed class FakeChatClient(string reply) : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
        => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply)));
    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
        => throw new NotSupportedException();
    public object? GetService(Type serviceType, object? serviceKey = null) => null;
    public void Dispose() { }
}

public class ReviewerTests
{
    private const string Diff = """
        diff --git a/src/Repo.cs b/src/Repo.cs
        new file mode 100644
        --- /dev/null
        +++ b/src/Repo.cs
        @@ -0,0 +1,3 @@
        +var sql = $"SELECT * FROM T WHERE Name = '{name}'";
        +var apiKey = "sk_live_1234567890abcdef";
        +Run(sql);
        """;

    [Fact]
    public async Task Merges_rule_and_llm_findings_and_drops_hallucinated_files()
    {
        const string reply = """
            {"summary":"Found issues","findings":[
              {"ruleId":"CG-SQLI","severity":"High","file":"src/Repo.cs","line":1,"title":"SQL injection","explanation":"interpolated"},
              {"ruleId":"CG-SQLI","severity":"High","file":"src/NotInDiff.cs","line":5,"title":"Hallucinated","explanation":"x"}
            ]}
            """;
        var run = await new Reviewer(new FakeChatClient(reply), [], rules: "").ReviewAsync(Diff);

        Assert.Contains(run.Result.Findings, f => f is { RuleId: "CG-SECRET", Line: 2, Source: "rule" });
        Assert.Contains(run.Result.Findings, f => f is { RuleId: "CG-SQLI", Line: 1, Source: "llm" });
        Assert.DoesNotContain(run.Result.Findings, f => f.File == "src/NotInDiff.cs");
    }

    [Fact]
    public async Task Unparseable_reply_still_returns_rule_findings()
    {
        var run = await new Reviewer(new FakeChatClient("sorry, I can't"), [], rules: "").ReviewAsync(Diff);
        Assert.Single(run.Result.Findings);
        Assert.Contains("could not be parsed", run.Result.Summary);
    }
}
