# CodeGuard — notes for Claude Code

AI pull-request reviewer for .NET, focused on Australian banking/insurance risks.
.NET 10 · ModelContextProtocol C# SDK · Microsoft.Extensions.AI (IChatClient).

## Commands
- Build: `dotnet build`
- Test (no LLM needed, must stay green): `dotnet test`
- List MCP tools: `dotnet run --project src/CodeGuard.Agent -- tools --repo .`
- Call a tool without an LLM: `dotnet run --project src/CodeGuard.Agent -- call --repo . --tool read_file --args '{"path":"README.md"}'`
- Review a patch: `dotnet run --project src/CodeGuard.Agent -- review --repo . --diff-file evals/cases/01-sql-injection/diff.patch`
- Evals: `dotnet run --project src/CodeGuard.Agent -- eval --repo . --out eval-results.md`

Provider comes from `CODEGUARD_PROVIDER` = `ollama` (default, free local) | `github` | `azure`. See README for env vars.

## Layout
- `src/CodeGuard.Core` — pure logic, no I/O to LLMs: DiffParser, SecretScanner (incl. TFN checksum), PathGuard, ReviewJson.
- `src/CodeGuard.McpServer` — MCP stdio server; tools in `RepoTools.cs`.
- `src/CodeGuard.Agent` — CLI + agent loop (`Reviewer.cs`), provider factory, eval runner, GitHub commenter.
- `rules/*.md` — rule IDs (CG-SQLI, CG-SECRET, …) injected into the system prompt.
- `evals/cases/NN-name/` — `files/`, `diff.patch` (line numbers must match files), `expected.json`.

## Conventions and guardrails
- Every MCP tool must resolve paths through `PathGuard.Resolve` and validate git refs with `SafeRef`.
- MCP server: never write to stdout except via the SDK (logs go to stderr).
- Execution tools stay behind `CODEGUARD_ALLOW_EXEC`. Do not enable it in CI for PRs.
- New deterministic rules go in `SecretScanner` with unit tests for a true positive AND a false positive.
- Test agent logic with a fake `IChatClient` (see `ReviewerTests.cs`); do not call real models in unit tests.
- Never commit real secrets, keys or real TFNs. Use synthetic data (123 456 782 is the standard sample TFN).
- Keep nullable enabled and the build at 0 warnings.
