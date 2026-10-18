using System.Globalization;
using System.Text.RegularExpressions;
using Avala.Forges.Contracts;
using Avala.Sdk;

namespace Avala.Forges.Watching;

internal static partial class PullRequestLinks
{
    private const string Punctuation = "._,;:!?";

    public static IEnumerable<Uri> In(string text) =>
        Link().Matches(text)
            .Select(found => Uri.TryCreate(found.Value.TrimEnd(Punctuation.ToCharArray()), UriKind.Absolute, out var link) ? link : null)
            .OfType<Uri>();

    public static int Settled(string text)
    {
        for (var index = text.Length - 1; index >= 0; index--)
        {
            if (!IsLinkCharacter(text[index]))
            {
                return index + 1;
            }
        }

        return 0;
    }

    public static bool LooksLikePullRequest(Uri link)
    {
        var parts = link.AbsolutePath.Trim('/').Split('/');

        return Enumerable.Range(2, Math.Max(0, parts.Length - 3)).Any(index => parts[index].Length > 0 && parts[index + 1].Length > 0 && parts[index + 1].All(char.IsAsciiDigit));
    }

    public static Option<int> NumberOf(Uri link, RepositoryAddress origin, IReadOnlySet<string> segments)
    {
        if (origin.Host.Contains('.', StringComparison.Ordinal)
            && !Same(link.Host, origin.Host)
            && !origin.Host.EndsWith($".{link.Host}", StringComparison.OrdinalIgnoreCase))
        {
            return Option<int>.None;
        }

        var parts = link.AbsolutePath.Trim('/').Split('/');

        for (var index = 2; index + 1 < parts.Length; index++)
        {
            if (Same(parts[index - 2], origin.Owner)
                && Same(parts[index - 1], origin.Name)
                && segments.Contains(parts[index])
                && int.TryParse(parts[index + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            {
                return number;
            }
        }

        return Option<int>.None;
    }

    private static bool IsLinkCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character) || "-._~:/?#@!$&+,;=%".Contains(character, StringComparison.Ordinal);

    private static bool Same(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"https?://[A-Za-z0-9\-._~:/?#@!$&+,;=%]+")]
    private static partial Regex Link();
}
