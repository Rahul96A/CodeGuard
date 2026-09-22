using System.Text.Json;
using CodeGuard.Agent;
using CodeGuard.Core;
using Microsoft.Extensions.AI;

// ---------------------------------------------------------------------------
//  CodeGuard – AI pull-request reviewer for .NET (MCP + Microsoft.Extensions.AI)
//
//  codeguard review  --repo . --base origin/main [--provider ollama|azure|none] [--out review.md] [--pr 12] [--fail-on High]
//  codeguard review  --diff-file changes.patch --repo .
//  codeguard eval    --cases evals/cases [--provider ...]
//  codeguard tools   --repo .                        (list MCP tools, no LLM needed)
//  codeguard call    --repo . --tool read_file --args '{"path":"README.md"}'   (test a tool, no LLM)
// ---------------------------------------------------------------------------

var command = args.FirstOrDefault(a => !a.StartsWith("--")) ?? "review";
string? Arg(string name) { var i = Array.IndexOf(args, "--" + name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }

var repo = Path.GetFullPath(Arg("repo") ?? ".");
var provider = Arg("provider") ?? Environment.GetEnvironmentVariable("CODEGUARD_PROVIDER") ?? "ollama";
// Whole-run budget. Local CPU models can be slow; raise with CODEGUARD_TIMEOUT_MINUTES.
var budget = int.TryParse(Environment.GetEnvironmentVariable("CODEGUARD_TIMEOUT_MINUTES"), out var mins) && mins > 0 ? mins : 15;
using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(budget));
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

try
{
    switch (command)
    {
        case "tools":
        {
            await using var mcp = await McpConnection.ConnectAsync(repo, cts.Token);
            foreach (var t in await mcp.ListToolsAsync(cancellationToken: cts.Token))
                Console.WriteLine($"{t.Name,-20} {t.Description}");
            return 0;
        }
        case "call":
        {
            await using var mcp = await McpConnection.ConnectAsync(repo, cts.Token);
            var toolArgs = JsonSerializer.Deserialize<Dictionary<string, object?>>(Arg("args") ?? "{}") ?? [];
            var result = await mcp.CallToolAsync(Arg("tool") ?? throw new ArgumentException("--tool is required"), toolArgs, cancellationToken: cts.Token);
            foreach (var block in result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>()) Console.WriteLine(block.Text);
            return result.IsError == true ? 1 : 0;
        }
        case "eval":
        {
            var (chat, description) = BuildChat(provider);
            var cases = Arg("cases") ?? Path.Combine(repo, "evals", "cases");
            return await EvalRunner.RunAsync(cases, chat, LoadRules(repo), description, Arg("out") ?? "eval-results.md", cts.Token);
        }
        case "review":
        {
            var diff = Arg("diff-file") is { } df
                ? await File.ReadAllTextAsync(df, cts.Token)
                : await GitDiffAsync(repo, Arg("base") ?? "origin/main");
            if (string.IsNullOrWhiteSpace(diff)) { Console.WriteLine("No changes to review."); return 0; }

            var (chat, description) = BuildChat(provider);

            // Rules-only mode never calls tools, so skip starting the MCP server.
            await using var mcp = chat is null ? null : await McpConnection.ConnectAsync(repo, cts.Token);
            var tools = mcp is null ? [] : (await mcp.ListToolsAsync(cancellationToken: cts.Token)).Cast<AITool>().ToList();

            Console.Error.WriteLine(chat is null
                ? $"Reviewing with {description}..."
                : $"Reviewing with {description} and {tools.Count} MCP tools...");
            var run = await new Reviewer(chat, tools, LoadRules(repo)).ReviewAsync(diff, cts.Token);
            var markdown = ReportRenderer.ToMarkdown(run, description);

            await File.WriteAllTextAsync(Arg("out") ?? "codeguard-review.md", markdown, cts.Token);
            Console.WriteLine(markdown);

            // Post to the PR when running in GitHub Actions.
            var ghToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
            var ghRepo = Environment.GetEnvironmentVariable("GITHUB_REPOSITORY");
            if (int.TryParse(Arg("pr"), out var pr) && ghToken is { Length: > 0 } && ghRepo is { Length: > 0 })
            {
                await new GitHubCommenter(ghToken, ghRepo).UpsertCommentAsync(pr, markdown, cts.Token);
                Console.Error.WriteLine($"Posted review to PR #{pr}.");
            }

            // Optional quality gate for CI.
            if (Enum.TryParse<Severity>(Arg("fail-on"), true, out var threshold) && run.Result.Findings.Any(f => f.Severity >= threshold))
            {
                Console.Error.WriteLine($"Failing: findings at or above {threshold}.");
                return 2;
            }
            return 0;
        }
        default:
            Console.Error.WriteLine($"Unknown command '{command}'. Use review, eval, tools or call.");
            return 1;
    }
}
catch (Exception ex) when (ex is not OperationCanceledException)
{
    Console.Error.WriteLine($"CodeGuard error: {ex.Message}");
    if (IsTimeout(ex))
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine($"The '{provider}' LLM provider did not answer in time.");
        if (provider.Equals("ollama", StringComparison.OrdinalIgnoreCase))
            Console.Error.WriteLine("  A local model on CPU can take minutes per call. Raise OLLAMA_TIMEOUT_SECONDS (default 600), free up RAM, or pick a smaller OLLAMA_MODEL.");
        Console.Error.WriteLine("  Or run --provider none for the deterministic rules only, with no LLM.");
    }
    else if (IsConnectionFailure(ex))
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine($"Could not reach the '{provider}' LLM provider.");
        if (provider.Equals("ollama", StringComparison.OrdinalIgnoreCase))
            Console.Error.WriteLine("  Start Ollama (`ollama serve`) and pull a model (`ollama pull qwen2.5-coder:7b`), or set OLLAMA_ENDPOINT.");
        Console.Error.WriteLine("  Alternatively: --provider azure, or --provider none to run the deterministic rules only, with no LLM.");
    }
    return 1;
}

static (IChatClient? Chat, string Description) BuildChat(string provider, int maxToolRounds = 8)
{
    var (inner, description) = ChatClientFactory.Create(provider);
    if (inner is null) return (null, description);
    var client = new ChatClientBuilder(inner)
        .UseFunctionInvocation(configure: f => f.MaximumIterationsPerRequest = maxToolRounds)
        .Build();
    return (client, description);
}

static bool IsTimeout(Exception ex)
{
    for (Exception? e = ex; e is not null; e = e.InnerException)
    {
        if (e is TimeoutException or TaskCanceledException) return true;
        if (e is AggregateException agg && agg.InnerExceptions.Any(IsTimeout)) return true;
    }
    return false;
}

static bool IsConnectionFailure(Exception ex)
{
    for (Exception? e = ex; e is not null; e = e.InnerException)
    {
        if (e is System.Net.Http.HttpRequestException or System.Net.Sockets.SocketException) return true;
        if (e is AggregateException agg && agg.InnerExceptions.Any(IsConnectionFailure)) return true;
    }
    return false;
}

static string LoadRules(string repo) =>
    RulesLoader.FindRulesDirectory(repo) is { } dir ? RulesLoader.Load(dir)
    : RulesLoader.FindRulesDirectory(AppContext.BaseDirectory) is { } d2 ? RulesLoader.Load(d2) : "";

static async Task<string> GitDiffAsync(string repo, string baseRef)
{
    var psi = new System.Diagnostics.ProcessStartInfo("git") { WorkingDirectory = repo, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var a in new[] { "diff", "--unified=3", $"{baseRef}...HEAD" }) psi.ArgumentList.Add(a);
    using var p = System.Diagnostics.Process.Start(psi)!;
    var output = await p.StandardOutput.ReadToEndAsync();
    await p.WaitForExitAsync();
    if (p.ExitCode != 0) throw new InvalidOperationException($"git diff failed: {await p.StandardError.ReadToEndAsync()}");
    return output;
}
