using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace CodeGuard.Agent;

/// <summary>Creates or updates a single CodeGuard comment on a PR (no comment spam on re-runs).</summary>
public sealed class GitHubCommenter(string token, string repository)
{
    private readonly HttpClient _http = CreateClient(token);

    public async Task UpsertCommentAsync(int prNumber, string body, CancellationToken ct = default)
    {
        var comments = await _http.GetFromJsonAsync<JsonElement>($"repos/{repository}/issues/{prNumber}/comments?per_page=100", ct);
        long? existingId = null;
        foreach (var c in comments.EnumerateArray())
            if (c.GetProperty("body").GetString()?.Contains(ReportRenderer.Marker) == true) existingId = c.GetProperty("id").GetInt64();

        var response = existingId is null
            ? await _http.PostAsJsonAsync($"repos/{repository}/issues/{prNumber}/comments", new { body }, ct)
            : await _http.PatchAsJsonAsync($"repos/{repository}/issues/comments/{existingId}", new { body }, ct);
        response.EnsureSuccessStatusCode();
    }

    private static HttpClient CreateClient(string token)
    {
        var http = new HttpClient { BaseAddress = new Uri("https://api.github.com/") };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CodeGuard", "1.0"));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return http;
    }
}
