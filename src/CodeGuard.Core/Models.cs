using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeGuard.Core;

public enum Severity { Info, Low, Medium, High, Critical }

/// <summary>One issue raised by CodeGuard, either by a deterministic rule or by the LLM.</summary>
public sealed record Finding(
    string RuleId,
    Severity Severity,
    string File,
    int Line,
    string Title,
    string Explanation,
    string? Suggestion = null,
    string Source = "llm");

public sealed record ReviewResult(string Summary, List<Finding> Findings);

/// <summary>A line added in a unified diff, with its line number in the new file.</summary>
public sealed record AddedLine(string File, int Line, string Text);

public static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}
