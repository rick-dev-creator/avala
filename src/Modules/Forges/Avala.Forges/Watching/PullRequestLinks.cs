using System.Globalization;
using System.Text.RegularExpressions;
using Avala.Forges.Contracts;
using Avala.Sdk;

namespace Avala.Forges.Watching;

internal static partial class PullRequestLinks
{
    public static IEnumerable<Uri> In(string text) =>
        Link().Matches(text)
            .Select(found => Uri.TryCreate(found.Value.TrimEnd('.', ',', ';', ':', '!', '?'), UriKind.Absolute, out var link) ? link : null)
            .OfType<Uri>();

    public static Option<int> NumberOf(Uri link, RepositoryAddress origin, IReadOnlySet<string> segments)
    {
        var parts = link.AbsolutePath.Trim('/').Split('/');

        return parts.Length >= 4
            && string.Equals(parts[^4], origin.Owner, StringComparison.OrdinalIgnoreCase)
            && string.Equals(parts[^3], origin.Name, StringComparison.OrdinalIgnoreCase)
            && segments.Contains(parts[^2])
            && int.TryParse(parts[^1], NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                ? number
                : Option<int>.None;
    }

    [GeneratedRegex(@"https?://[^\s<>""')\]]+")]
    private static partial Regex Link();
}
