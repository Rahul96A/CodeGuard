using System.Text.Json;

namespace CodeGuard.Core;

/// <summary>Robustly extracts the JSON review object from an LLM reply (handles ``` fences and chatter).</summary>
public static class ReviewJson
{
    public static ReviewResult? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        try
        {
            var result = JsonSerializer.Deserialize<ReviewResult>(text[start..(end + 1)], Json.Options);
            return result is null ? null : result with { Findings = result.Findings ?? [] };
        }
        catch (JsonException) { return null; }
    }
}
