using System.Buffers;

namespace Avala.Server.Instances.Contracts.Authentication;

internal static class Base64UrlAlphabet
{
    public static SearchValues<char> Characters { get; } =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_");
}
