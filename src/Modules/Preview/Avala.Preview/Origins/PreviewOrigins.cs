using System.Collections.Immutable;
using Avala.Sdk;

namespace Avala.Preview.Origins;

internal readonly record struct PortBlock(int First, int Last)
{
    public bool Holds(int port) => port >= First && port <= Last;
}

internal sealed record PreviewScope(Option<PortBlock> Lease, ImmutableHashSet<int> Listening, string Worktree)
{
    public static PreviewScope Closed { get; } = new(Option<PortBlock>.None, [], string.Empty);
}

internal static class PreviewOrigins
{
    private static readonly string[] LoopbackHosts = ["localhost", "127.0.0.1", "[::1]", "::1"];

    private static readonly string[] InlineSchemes = ["data", "blob", "about"];

    public static Option<int> LoopbackPort(Uri address) =>
        address.IsAbsoluteUri
        && address.Scheme is "http" or "ws"
        && address.UserInfo.Length == 0
        && LoopbackHosts.Contains(address.Host, StringComparer.OrdinalIgnoreCase)
            ? address.Port
            : Option<int>.None;

    public static bool IsInside(string folder, string path)
    {
        if (folder.Length == 0 || path.Length == 0)
        {
            return false;
        }

        var root = folder.TrimEnd('/', '\\');
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        return path.Length > root.Length
            && path.StartsWith(root, comparison)
            && path[root.Length] is '/' or '\\';
    }

    extension(PreviewScope scope)
    {
        public bool Admits(Uri address, Option<string> realFile) =>
            LoopbackPort(address).Match(
                port => scope.Lease.Match(lease => lease.Holds(port), () => false) || scope.Listening.Contains(port),
                () => address.IsAbsoluteUri && address.IsFile && realFile.Match(path => IsInside(scope.Worktree, path), () => false));

        public bool AdmitsResource(Uri address, bool network, Option<string> realFile) =>
            network
            || (address.IsAbsoluteUri && InlineSchemes.Contains(address.Scheme, StringComparer.OrdinalIgnoreCase))
            || scope.Admits(address, realFile);
    }
}
