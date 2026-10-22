using Avala.Forges.Contracts;
using Avala.Sdk;

namespace Avala.Forges.Policy;

internal static class RemoteAddresses
{
    private const string GitSuffix = ".git";

    public static Option<RepositoryAddress> Parse(string remote)
    {
        var trimmed = remote.Trim();

        if (trimmed.Length == 0)
        {
            return Option<RepositoryAddress>.None;
        }

        if (ScpLike(trimmed) is { } scp)
        {
            return Address(scp.Host, scp.Path);
        }

        if (trimmed.Contains("://", StringComparison.Ordinal) && Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return Address(uri.IsFile ? string.Empty : uri.Host, Uri.UnescapeDataString(uri.AbsolutePath));
        }

        return IsAbsolutePath(trimmed) ? Address(string.Empty, trimmed) : Option<RepositoryAddress>.None;
    }

    public static bool Same(RepositoryAddress left, RepositoryAddress right) =>
        string.Equals(left.Owner, right.Owner, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.Name, right.Name, StringComparison.OrdinalIgnoreCase)
        && (string.Equals(left.Host, right.Host, StringComparison.OrdinalIgnoreCase)
            || left.Host.EndsWith($".{right.Host}", StringComparison.OrdinalIgnoreCase)
            || right.Host.EndsWith($".{left.Host}", StringComparison.OrdinalIgnoreCase));

    public static bool Same(string left, string right) =>
        left == right || Parse(left).Bind(one => Parse(right).Map(other => one.Host.Length == 0 && other.Host.Length == 0
            ? string.Equals(LocalPath(left), LocalPath(right), StringComparison.Ordinal)
            : Same(one, other))).Match(same => same, () => false);

    public static string Anonymous(string remote) =>
        remote.Contains("://", StringComparison.Ordinal) && Uri.TryCreate(remote.Trim(), UriKind.Absolute, out var uri) && uri.UserInfo.Length > 0
            ? new UriBuilder(uri) { UserName = string.Empty, Password = string.Empty }.Uri.AbsoluteUri
            : remote;

    private static string LocalPath(string remote)
    {
        var trimmed = remote.Trim();
        var path = trimmed.Contains("://", StringComparison.Ordinal) && Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ? uri.LocalPath : trimmed;
        var full = string.Join('/', path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).Where(segment => segment != "."));

        return full.EndsWith(GitSuffix, StringComparison.OrdinalIgnoreCase) ? full[..^GitSuffix.Length] : full;
    }

    private static bool IsAbsolutePath(string path) =>
        path[0] is '/' or '\\'
        || (path.Length > 2 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '/' or '\\');

    private static (string Host, string Path)? ScpLike(string remote)
    {
        var colon = remote.IndexOf(':', StringComparison.Ordinal);

        if (colon <= 0 || remote.Contains("://", StringComparison.Ordinal) || (colon == 1 && char.IsAsciiLetter(remote[0])))
        {
            return null;
        }

        var host = remote[..colon];
        var at = host.LastIndexOf('@');

        return (at >= 0 ? host[(at + 1)..] : host, remote[(colon + 1)..]);
    }

    private static Option<RepositoryAddress> Address(string host, string path)
    {
        var segments = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length < 2)
        {
            return Option<RepositoryAddress>.None;
        }

        var name = segments[^1].EndsWith(GitSuffix, StringComparison.OrdinalIgnoreCase) ? segments[^1][..^GitSuffix.Length] : segments[^1];

        return name.Length == 0 ? Option<RepositoryAddress>.None : new RepositoryAddress(host, segments[^2], name);
    }
}
