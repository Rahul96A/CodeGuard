using ModelContextProtocol.Client;

namespace CodeGuard.Agent;

/// <summary>Starts the CodeGuard MCP server as a child process and connects to it over stdio.</summary>
public static class McpConnection
{
    public static async Task<McpClient> ConnectAsync(string repoRoot, CancellationToken ct = default)
    {
        // The server project is referenced by the agent, so its DLL sits next to ours.
        var serverDll = Path.Combine(AppContext.BaseDirectory, "CodeGuard.McpServer.dll");
        if (!File.Exists(serverDll)) throw new FileNotFoundException("MCP server not found. Build the solution first.", serverDll);

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "codeguard-repo-tools",
            Command = "dotnet",
            Arguments = [serverDll, "--root", Path.GetFullPath(repoRoot)],
        });
        return await McpClient.CreateAsync(transport, cancellationToken: ct);
    }
}
