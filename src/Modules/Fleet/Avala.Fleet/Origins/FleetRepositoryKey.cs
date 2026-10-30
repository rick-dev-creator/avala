using System.Buffers;
using Avala.Sdk;

namespace Avala.Fleet.Origins;

internal static class FleetRepositoryKey
{
    public const int MaximumLength = 300;

    private const string Origin = "origin";

    private const string GitSuffix = ".git";

    private static readonly SearchValues<char> HostCharacters = SearchValues.Create("abcdefghijklmnopqrstuvwxyz0123456789.-");

    private static readonly SearchValues<char> SegmentCharacters = SearchValues.Create("abcdefghijklmnopqrstuvwxyz0123456789._-");

    private static readonly SearchValues<char> Hexadecimal = SearchValues.Create("0123456789abcdef");

    private static readonly string[] NetworkSchemes = ["https", "http", "ssh", "git"];

    public static Option<string> From(IReadOnlyList<(string Name, string Url)> remotes, IReadOnlyList<string> roots)
    {
        var remote = remotes
            .OrderBy(candidate => candidate.Name == Origin ? 0 : 1)
            .ThenBy(candidate => candidate.Name, StringComparer.Ordinal)
            .Select(candidate => Remote(candidate.Url))
            .FirstOrDefault(key => key.IsSome);

        return remote.IsSome ? remote : roots
            .Select(root => root.Trim().ToLowerInvariant())
            .Where(root => root.Length is 40 or 64 && !root.AsSpan().ContainsAnyExcept(Hexadecimal))
            .Order(StringComparer.Ordinal)
            .Select(root => Option<string>.Some($"root:{root}"))
            .FirstOrDefault();
    }

    private static Option<string> Remote(string url)
    {
        var (host, path) = Split(url.Trim());
        var lowered = path.ToLowerInvariant().Trim('/');
        var bare = lowered.EndsWith(GitSuffix, StringComparison.Ordinal) ? lowered[..^GitSuffix.Length].TrimEnd('/') : lowered;
        var key = $"remote:{host.ToLowerInvariant()}/{bare}";

        return host.Length > 0 && Shaped(host.ToLowerInvariant(), bare) && key.Length <= MaximumLength ? key : Option<string>.None;
    }

    private static (string Host, string Path) Split(string url)
    {
        if (url.Contains("://", StringComparison.Ordinal))
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var parsed) && NetworkSchemes.Contains(parsed.Scheme, StringComparer.OrdinalIgnoreCase)
                ? (parsed.Host, parsed.AbsolutePath)
                : (string.Empty, string.Empty);
        }

        var colon = url.IndexOf(':', StringComparison.Ordinal);
        var authority = colon > 0 ? url[..colon] : string.Empty;
        var host = authority[(authority.LastIndexOf('@') + 1)..];

        return colon > 0 && !url.Contains('\\', StringComparison.Ordinal) && !host.Contains('/', StringComparison.Ordinal)
            ? (host, url[(colon + 1)..])
            : (string.Empty, string.Empty);
    }

    private static bool Shaped(string host, string path)
    {
        var segments = path.Split('/');

        return host.Contains('.', StringComparison.Ordinal)
            && !host.AsSpan().ContainsAnyExcept(HostCharacters)
            && segments.Length >= 2
            && segments.All(segment => segment.Length > 0 && !segment.AsSpan().ContainsAnyExcept(SegmentCharacters));
    }
}
