namespace Avala.Server.Instances.Contracts.Authentication;

internal static class InstanceKeyShape
{
    public const string Prefix = "avk_";

    public const string Masked = Prefix + "…";

    public const int Length = 47;

    public static bool Matches(string text) =>
        text.Length == Length
        && text.StartsWith(Prefix, StringComparison.Ordinal)
        && !text.AsSpan(Prefix.Length).ContainsAnyExcept(Base64UrlAlphabet.Characters);
}
