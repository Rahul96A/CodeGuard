using System.ClientModel;
using System.ClientModel.Primitives;
using Azure.Identity;
using Microsoft.Extensions.AI;
using OpenAI;

namespace CodeGuard.Agent;

/// <summary>
/// One IChatClient abstraction, three backends. All three speak the OpenAI-compatible API,
/// so the rest of the code never knows which one is in use.
///   ollama : free, local (http://localhost:11434/v1)
///   github : free tier with rate limits (GitHub Models)
///   azure  : Microsoft Foundry (pay per token; use trial credit + a budget alert)
/// </summary>
public static class ChatClientFactory
{
    public static (IChatClient Client, string Description) Create(string provider)
    {
        switch (provider.ToLowerInvariant())
        {
            case "ollama":
            {
                var endpoint = Env("OLLAMA_ENDPOINT") ?? "http://localhost:11434/v1";
                var model = Env("OLLAMA_MODEL") ?? "qwen2.5-coder:7b";
                var client = new OpenAIClient(new ApiKeyCredential("ollama"), new OpenAIClientOptions { Endpoint = new Uri(endpoint) });
                return (client.GetChatClient(model).AsIChatClient(), $"Ollama ({model})");
            }
            case "github":
            {
                var token = Env("GITHUB_TOKEN") ?? throw new InvalidOperationException("Set GITHUB_TOKEN (a PAT with 'models:read', or the Actions token).");
                var model = Env("GITHUB_MODEL") ?? "openai/gpt-4o-mini";
                var client = new OpenAIClient(new ApiKeyCredential(token), new OpenAIClientOptions { Endpoint = new Uri("https://models.github.ai/inference") });
                return (client.GetChatClient(model).AsIChatClient(), $"GitHub Models ({model})");
            }
            case "azure":
            {
                // Foundry resource endpoint, e.g. https://<resource>.openai.azure.com  or  https://<resource>.services.ai.azure.com
                var endpoint = (Env("AZURE_OPENAI_ENDPOINT") ?? throw new InvalidOperationException("Set AZURE_OPENAI_ENDPOINT.")).TrimEnd('/');
                var deployment = Env("AZURE_OPENAI_DEPLOYMENT") ?? "gpt-4o-mini";
                var options = new OpenAIClientOptions { Endpoint = new Uri($"{endpoint}/openai/v1/") };
                var key = Env("AZURE_OPENAI_API_KEY");

#pragma warning disable OPENAI001 // Entra ID auth policy is marked experimental in the OpenAI SDK
                // Keyless (recommended): 'az login' locally, managed identity in Azure. Falls back to API key if set.
                var client = key is null
                    ? new OpenAIClient(new BearerTokenPolicy(new DefaultAzureCredential(), "https://ai.azure.com/.default"), options)
                    : new OpenAIClient(new ApiKeyCredential(key), options);
#pragma warning restore OPENAI001
                return (client.GetChatClient(deployment).AsIChatClient(), $"Microsoft Foundry ({deployment}, {(key is null ? "Entra ID" : "API key")})");
            }
            default:
                throw new ArgumentException($"Unknown provider '{provider}'. Use ollama, github or azure.");
        }
    }

    private static string? Env(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : null;
}
