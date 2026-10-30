using System.Buffers;
using Avala.Fleet.Facts;
using Avala.Sdk;

namespace Avala.Fleet.Connection;

internal enum ConnectionState
{
    NotConnected,
    Connected,
    Paused,
    KeyRefused,
    KeychainUnavailable,
}

internal sealed record ServerAddress(string Origin)
{
    public static Result<ServerAddress, FleetError> Create(string text)
    {
        if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out var address)
            || address.Scheme is not ("https" or "http")
            || address.UserInfo.Length > 0
            || address.AbsolutePath != "/"
            || address.Query.Length > 0
            || address.Fragment.Length > 0)
        {
            return FleetError.InvalidAddress;
        }

        if (address.Scheme == "http" && !address.IsLoopback)
        {
            return FleetError.InsecureAddress;
        }

        return new ServerAddress(address.GetLeftPart(UriPartial.Authority));
    }
}

internal static class KeyShape
{
    public const string Prefix = "avk_";

    public const int Length = 47;

    private static readonly SearchValues<char> Base64Url = SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_");

    public static bool Matches(string key) =>
        key.Length == Length && key.StartsWith(Prefix, StringComparison.Ordinal) && !key.AsSpan(Prefix.Length).ContainsAnyExcept(Base64Url);
}

internal sealed record FleetScope(string Server, Guid Instance)
{
    public string Id => $"{Server}|{Instance:D}";
}
