using System.Collections;
using System.Text.RegularExpressions;

namespace Avala.Runtime.Diagnostics;

internal sealed partial class LogRedaction(IReadOnlyList<string> secrets)
{
    public const string Mark = "[redacted]";

    private const int ShortestSecret = 8;

    private static readonly string[] SecretWords = ["KEY", "TOKEN", "SECRET", "PASSWORD", "CREDENTIAL"];

    public static LogRedaction FromEnvironment() =>
        From(Environment.GetEnvironmentVariables().Cast<DictionaryEntry>().ToDictionary(entry => (string)entry.Key, entry => entry.Value as string ?? string.Empty));

    public static LogRedaction From(IReadOnlyDictionary<string, string> variables) =>
        new(
        [
            .. variables
                .Where(variable => variable.Value.Length >= ShortestSecret && SecretWords.Any(word => variable.Key.Contains(word, StringComparison.OrdinalIgnoreCase)))
                .Select(variable => variable.Value)
                .Distinct(StringComparer.Ordinal)
                .OrderByDescending(secret => secret.Length),
        ]);

    public string Apply(string text) =>
        Patterns().Replace(secrets.Aggregate(text, (redacted, secret) => redacted.Replace(secret, Mark, StringComparison.Ordinal)), Mark);

    [GeneratedRegex(@"sk-ant-[A-Za-z0-9_\-]+|(?<=\bBearer\s+)[A-Za-z0-9._~+/=\-]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Patterns();
}
