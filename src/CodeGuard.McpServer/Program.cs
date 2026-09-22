using CodeGuard.McpServer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// CodeGuard MCP server: exposes repository tools over stdio.
// Usage: dotnet CodeGuard.McpServer.dll --root <repo path>
var builder = Host.CreateApplicationBuilder(args);

// stdout carries the MCP protocol, so ALL logs must go to stderr.
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

var root = builder.Configuration["root"] ?? Environment.GetEnvironmentVariable("CODEGUARD_ROOT") ?? Directory.GetCurrentDirectory();
var allowExec = string.Equals(Environment.GetEnvironmentVariable("CODEGUARD_ALLOW_EXEC"), "true", StringComparison.OrdinalIgnoreCase);

builder.Services.AddSingleton(new RepoContext(Path.GetFullPath(root), allowExec));
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
