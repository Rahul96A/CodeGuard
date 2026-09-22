using System.Text;
using System.Text.Json;

namespace CodeGuard.Core;

/// <summary>
/// Robustly extracts the JSON review object from an LLM reply: handles ``` fences and chatter,
/// and repairs replies that stop a few closing brackets early (small local models often emit
/// their end-of-turn token right before the final "}" of the object).
/// </summary>
public static class ReviewJson
{
    public static ReviewResult? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var start = text.IndexOf('{');
        if (start < 0) return null;

        var end = text.LastIndexOf('}');
        if (end > start && Deserialize(text[start..(end + 1)]) is { } exact) return exact;

        // Truncated reply: keep everything from the first "{" and append whatever closers are missing.
        var tail = text[start..].TrimEnd();
        if (tail.EndsWith("```", StringComparison.Ordinal)) tail = tail[..^3].TrimEnd();
        var closers = MissingClosers(tail);
        return closers.Length == 0 ? null : Deserialize(tail + closers);
    }

    private static ReviewResult? Deserialize(string json)
    {
        try
        {
            var result = JsonSerializer.Deserialize<ReviewResult>(json, Json.Options);
            return result is null ? null : result with { Findings = result.Findings ?? [] };
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Walks the text outside string literals and returns the closers needed to balance it.</summary>
    private static string MissingClosers(string s)
    {
        var stack = new Stack<char>();
        var inString = false;
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (inString)
            {
                if (c == '\\') i++;
                else if (c == '"') inString = false;
                continue;
            }
            switch (c)
            {
                case '"': inString = true; break;
                case '{': stack.Push('}'); break;
                case '[': stack.Push(']'); break;
                case '}' or ']':
                    if (stack.Count == 0 || stack.Pop() != c) return ""; // malformed, do not guess
                    break;
            }
        }
        if (inString) return ""; // cut mid-string: too little to trust
        var sb = new StringBuilder();
        while (stack.Count > 0) sb.Append(stack.Pop());
        return sb.ToString();
    }
}
