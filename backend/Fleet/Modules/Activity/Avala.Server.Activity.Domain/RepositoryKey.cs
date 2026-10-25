using System.Buffers;

namespace Avala.Server.Activity.Domain;

internal sealed record RepositoryKey(string Value)
{
    public const int MaximumLength = 300;

    public const string RootPrefix = "root:";

    public const string RemotePrefix = "remote:";

    private const string GitSuffix = ".git";

    private static readonly SearchValues<char> Hexadecimal = SearchValues.Create("0123456789abcdef");

    private static readonly SearchValues<char> HostCharacters = SearchValues.Create("abcdefghijklmnopqrstuvwxyz0123456789.-");

    private static readonly SearchValues<char> SegmentCharacters = SearchValues.Create("abcdefghijklmnopqrstuvwxyz0123456789._-");

    public static Admission<RepositoryKey> Create(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Admission.Refuse<RepositoryKey>("a repository key is required");
        }

        if (text.Length > MaximumLength)
        {
            return Admission.Refuse<RepositoryKey>($"the repository key is longer than {MaximumLength} characters");
        }

        var lowered = text.ToLowerInvariant();

        return lowered.StartsWith(RootPrefix, StringComparison.Ordinal) ? Root(lowered[RootPrefix.Length..])
            : lowered.StartsWith(RemotePrefix, StringComparison.Ordinal) ? Remote(lowered[RemotePrefix.Length..])
            : Admission.Refuse<RepositoryKey>($"a repository key starts with '{RootPrefix}' or '{RemotePrefix}'");
    }

    private static Admission<RepositoryKey> Root(string hash) =>
        hash.Length is 40 or 64 && !hash.AsSpan().ContainsAnyExcept(Hexadecimal)
            ? Admission.Admit(new RepositoryKey(RootPrefix + hash))
            : Admission.Refuse<RepositoryKey>("a root key is the root commit's hash, 40 or 64 hexadecimal characters");

    private static Admission<RepositoryKey> Remote(string remote)
    {
        if (Flaw(remote) is { } flaw)
        {
            return Admission.Refuse<RepositoryKey>($"the repository key {flaw}");
        }

        var trimmed = remote.TrimEnd('/');
        trimmed = trimmed.EndsWith(GitSuffix, StringComparison.Ordinal) ? trimmed[..^GitSuffix.Length].TrimEnd('/') : trimmed;
        var segments = trimmed.Split('/');

        return Shape(segments) is { } refusal
            ? Admission.Refuse<RepositoryKey>($"the repository key {refusal}")
            : Admission.Admit(new RepositoryKey(RemotePrefix + trimmed));
    }

    private static string? Flaw(string remote) =>
        remote.Contains('@', StringComparison.Ordinal) ? "carries credentials"
        : remote.Contains("://", StringComparison.Ordinal) ? "carries a scheme"
        : remote.Contains(':', StringComparison.Ordinal) ? "carries a port"
        : remote.Contains('%', StringComparison.Ordinal) ? "is percent-encoded"
        : remote.Any(char.IsWhiteSpace) ? "contains whitespace"
        : null;

    private static string? Shape(string[] segments) =>
        segments is not [var host, _, _, ..] ? "names a host and at least two path segments"
        : segments.Any(segment => segment.Length == 0) ? "has an empty segment"
        : host.AsSpan().ContainsAnyExcept(HostCharacters) || !host.Contains('.', StringComparison.Ordinal) ? "has an invalid host"
        : segments.Skip(1).Any(segment => segment.AsSpan().ContainsAnyExcept(SegmentCharacters)) ? "has an invalid path segment"
        : null;
}
