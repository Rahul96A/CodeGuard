using System.Text.RegularExpressions;

namespace CodeGuard.Core;

/// <summary>
/// Deterministic, zero-cost checks that run before the LLM. Cheap rules catch the
/// obvious problems reliably; the LLM is used for things regex cannot judge.
/// </summary>
public static partial class SecretScanner
{
    private sealed record Rule(string Id, Severity Severity, string Title, Regex Pattern, string Suggestion);

    private static readonly Rule[] Rules =
    [
        new("CG-SECRET", Severity.Critical, "AWS access key committed", AwsKey(),
            "Remove the key, rotate it, and load it from Azure Key Vault or environment configuration."),
        new("CG-SECRET", Severity.Critical, "Private key committed", PrivateKey(),
            "Remove the key from source control and rotate it."),
        new("CG-SECRET", Severity.Critical, "GitHub token committed", GitHubToken(),
            "Revoke the token and use GitHub Actions secrets instead."),
        new("CG-SECRET", Severity.High, "Azure storage account key in source", AzureStorageKey(),
            "Use Managed Identity or Key Vault references instead of account keys."),
        new("CG-SECRET", Severity.High, "Connection string with an inline password", ConnStringPassword(),
            "Use Managed Identity (Authentication=Active Directory Default) or Key Vault."),
        new("CG-SECRET", Severity.High, "Hard-coded password or API key", GenericSecret(),
            "Move the value to configuration backed by Key Vault or user-secrets."),
    ];

    public static List<Finding> Scan(IEnumerable<AddedLine> lines)
    {
        var findings = new List<Finding>();
        foreach (var l in lines)
        {
            foreach (var r in Rules)
            {
                if (r.Pattern.IsMatch(l.Text))
                {
                    findings.Add(new Finding(r.Id, r.Severity, l.File, l.Line, r.Title,
                        "Matched a deterministic secret-detection rule.", r.Suggestion, Source: "rule"));
                    break; // one secret finding per line is enough
                }
            }

            if (TfnContext().IsMatch(l.Text))
            {
                foreach (Match m in NineDigits().Matches(l.Text))
                {
                    if (IsValidTfn(m.Value))
                    {
                        findings.Add(new Finding("CG-PII", Severity.High, l.File, l.Line,
                            "Possible Australian Tax File Number in source",
                            "A value passing the TFN checksum appears next to TFN/tax wording. TFNs are protected under the Privacy (Tax File Number) Rule.",
                            "Remove real identifiers; use synthetic test data and mask TFNs in logs.", Source: "rule"));
                        break;
                    }
                }
            }
        }
        return findings;
    }

    /// <summary>ATO TFN checksum: weighted sum of the 9 digits must be divisible by 11.</summary>
    public static bool IsValidTfn(string value)
    {
        var digits = value.Where(char.IsDigit).Select(c => c - '0').ToArray();
        if (digits.Length != 9) return false;
        int[] weights = [1, 4, 3, 7, 5, 8, 6, 9, 10];
        var sum = 0;
        for (var i = 0; i < 9; i++) sum += digits[i] * weights[i];
        return sum % 11 == 0;
    }

    [GeneratedRegex(@"\bAKIA[0-9A-Z]{16}\b")] private static partial Regex AwsKey();
    [GeneratedRegex(@"-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----")] private static partial Regex PrivateKey();
    [GeneratedRegex(@"\b(ghp|gho|ghs|github_pat)_[A-Za-z0-9_]{20,}")] private static partial Regex GitHubToken();
    [GeneratedRegex(@"AccountKey=[A-Za-z0-9+/=]{40,}", RegexOptions.IgnoreCase)] private static partial Regex AzureStorageKey();
    [GeneratedRegex(@"(?:^|[;""])\s*(Password|Pwd)\s*=\s*[^;""'\s]{4,}", RegexOptions.IgnoreCase)] private static partial Regex ConnStringPassword();
    [GeneratedRegex(@"(password|passwd|secret|api[_-]?key|apikey|token)\w*""?\s*[:=]\s*""[^""\s]{8,}""", RegexOptions.IgnoreCase)] private static partial Regex GenericSecret();
    [GeneratedRegex(@"tfn|tax\s*file|taxfile", RegexOptions.IgnoreCase)] private static partial Regex TfnContext();
    [GeneratedRegex(@"\b\d{3}[ -]?\d{3}[ -]?\d{3}\b")] private static partial Regex NineDigits();
}
