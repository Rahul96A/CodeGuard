using CodeGuard.Core;

namespace CodeGuard.Core.Tests;

public class DiffParserTests
{
    private const string Diff = """
        diff --git a/src/A.cs b/src/A.cs
        index 111..222 100644
        --- a/src/A.cs
        +++ b/src/A.cs
        @@ -10,4 +10,5 @@ public class A
         context line 10
        -removed line
        +added line 11
        +added line 12
         context line 13
        """;

    [Fact]
    public void Tracks_new_file_line_numbers()
    {
        var added = DiffParser.ParseAddedLines(Diff);
        Assert.Equal(2, added.Count);
        Assert.Equal(("src/A.cs", 11, "added line 11"), (added[0].File, added[0].Line, added[0].Text));
        Assert.Equal(12, added[1].Line);
    }

    [Fact]
    public void Removed_line_starting_with_dashes_is_not_treated_as_header()
    {
        var diff = "diff --git a/x.sql b/x.sql\n--- a/x.sql\n+++ b/x.sql\n@@ -1,2 +1,2 @@\n--- old comment\n+-- new comment\n keep\n";
        var added = DiffParser.ParseAddedLines(diff);
        Assert.Single(added);
        Assert.Equal("x.sql", added[0].File);
        Assert.Equal(1, added[0].Line);
    }
}

public class SecretScannerTests
{
    [Theory]
    [InlineData("123456782", true)]   // valid checksum (well-known sample)
    [InlineData("123 456 782", true)]
    [InlineData("123456789", false)]
    [InlineData("12345678", false)]
    public void Tfn_checksum(string value, bool expected) => Assert.Equal(expected, SecretScanner.IsValidTfn(value));

    [Theory]
    [InlineData("var key = \"AKIAIOSFODNN7EXAMPLE\";")]
    [InlineData("\"Server=x;User ID=sa;Password=Sup3rSecret;\"")]
    [InlineData("const string ApiKey = \"abcd1234efgh5678\";")]
    [InlineData("-----BEGIN RSA PRIVATE KEY-----")]
    public void Detects_secrets(string line)
    {
        var findings = SecretScanner.Scan([new AddedLine("f.cs", 1, line)]);
        Assert.Contains(findings, f => f.RuleId == "CG-SECRET");
    }

    [Fact]
    public void Tfn_needs_context_word()
    {
        Assert.Empty(SecretScanner.Scan([new AddedLine("f.cs", 1, "var orderId = 123456782;")]));
        Assert.Single(SecretScanner.Scan([new AddedLine("f.cs", 1, "var tfn = \"123456782\";")]));
    }

    [Fact]
    public void Ignores_config_lookup() =>
        Assert.Empty(SecretScanner.Scan([new AddedLine("f.cs", 1, "var pwd = config[\"Db:Password\"];")]));
}

public class ReviewJsonTests
{
    [Fact]
    public void Parses_fenced_json_with_chatter()
    {
        var text = "Here you go:\n```json\n{\"summary\":\"ok\",\"findings\":[{\"ruleId\":\"CG-SQLI\",\"severity\":\"High\",\"file\":\"a.cs\",\"line\":3,\"title\":\"t\",\"explanation\":\"e\"}]}\n```";
        var r = ReviewJson.TryParse(text);
        Assert.NotNull(r);
        Assert.Equal(Severity.High, r!.Findings[0].Severity);
    }

    [Fact]
    public void Returns_null_on_garbage() => Assert.Null(ReviewJson.TryParse("no json here"));

    [Fact]
    public void Repairs_reply_that_stops_before_the_final_closing_brace()
    {
        // qwen2.5-coder on Ollama frequently ends the turn right after "}]" and omits the last "}".
        var text = "{\"summary\":\"s\",\"findings\":[{\"ruleId\":\"CG-SQLI\",\"severity\":\"High\",\"file\":\"a.cs\",\"line\":11,\"title\":\"t\",\"explanation\":\"e\"}]";
        var r = ReviewJson.TryParse(text);
        Assert.NotNull(r);
        Assert.Equal(11, Assert.Single(r!.Findings).Line);
    }

    [Fact]
    public void Repairs_reply_missing_several_closers_inside_a_fence()
    {
        var text = """
            ```json
            {"summary":"s","findings":[{"ruleId":"CG-PII","severity":"Low","file":"a.cs","line":2,"title":"t","explanation":"braces { in [ strings ] are ignored"
            ```
            """;
        var r = ReviewJson.TryParse(text);
        Assert.NotNull(r);
        Assert.Equal("CG-PII", Assert.Single(r!.Findings).RuleId);
    }

    [Fact]
    public void Does_not_guess_when_cut_mid_string_or_malformed()
    {
        Assert.Null(ReviewJson.TryParse("{\"summary\":\"cut off he"));
        Assert.Null(ReviewJson.TryParse("{\"summary\":\"s\",\"findings\":[}"));
    }
}

public class PathGuardTests
{
    [Fact]
    public void Blocks_traversal() =>
        Assert.Throws<UnauthorizedAccessException>(() => PathGuard.Resolve("/repo", "../etc/passwd"));

    [Fact]
    public void Allows_inside_root() =>
        Assert.EndsWith(Path.Combine("repo", "src", "a.cs"), PathGuard.Resolve("/repo", "src/a.cs"));
}
