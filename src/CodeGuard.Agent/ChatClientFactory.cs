using System.ClientModel;
using System.ClientModel.Primitives;
using Azure.Identity;
using Microsoft.Extensions.AI;
using OpenAI;

namespace CodeGuard.Agent;

/// <summary>
/// One IChatClient abstraction, two backends. Both speak the OpenAI-compatible API,
/// so the rest of the code never knows which one is in use.
///   ollama : free, local (http://localhost:11434/v1)
///   azure  : Microsoft Foundry (pay per token; use trial credit + a budget alert)
///   none   : no LLM at all; only the deterministic rules run (offline / air-gapped CI)
/// </summary>
public static class ChatClientFactory
{
    public static (IChatClient? Client, string Description) Create(string provider)
    {
        switch (provider.ToLowerInvariant())
        {
            case "none":
            case "rules":
                return (null, "deterministic rules only (no LLM)");
            case "ollama":
            {
                var endpoint = Env("OLLAMA_ENDPOINT") ?? "http://localhost:11434/v1";
                var model = Env("OLLAMA_MODEL") ?? "qwen2.5-coder:7b";
                // A local model on CPU can take minutes per call, far beyond the SDK's 100 s default,
                // and retrying a slow local request only multiplies the wait. Tune with OLLAMA_TIMEOUT_SECONDS.
                var timeout = int.TryParse(Env("OLLAMA_TIMEOUT_SECONDS"), out var s) && s > 0 ? s : 600;
                var options = new OpenAIClientOptions
                {
                    Endpoint = new Uri(endpoint),
                    NetworkTimeout = TimeSpan.FromSeconds(timeout),
                    RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
                };
                var client = new OpenAIClient(new ApiKeyCredential("ollama"), options);
                return (client.GetChatClient(model).AsIChatClient(), $"Ollama ({model})");
            }
            case "github":
                throw new ArgumentException("GitHub Models was retired on 30 July 2026. Use ollama, azure or none.");
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
                throw new ArgumentException($"Unknown provider '{provider}'. Use ollama, azure or none.");
        }
    }

    private static string? Env(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : null;
}
