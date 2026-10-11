using System.Security.Cryptography;
using System.Text;
using Avala.Forges.Contracts;
using Avala.Triggers.Contracts;

namespace Avala.Forges.Sifting;

internal static class HookVerification
{
    public static DeliveryVerdict Check(HookSignature signature, IReadOnlyDictionary<string, string> headers, ReadOnlySpan<byte> body, string secret)
    {
        var sent = signature.Headers
            .Select(name => headers.FirstOrDefault(header => string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase)).Value)
            .FirstOrDefault(value => !string.IsNullOrEmpty(value));

        if (sent is null)
        {
            return DeliveryVerdict.NotSigned;
        }

        return sent.StartsWith(signature.Prefix, StringComparison.Ordinal) && Matches(secret, body, sent[signature.Prefix.Length..])
            ? DeliveryVerdict.Accepted
            : DeliveryVerdict.BadSignature;
    }

    public static string Sign(HookSignature signature, string secret, ReadOnlySpan<byte> body) =>
        signature.Prefix + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body));

    private static bool Matches(string secret, ReadOnlySpan<byte> body, string hex) =>
        hex.Length == SHA256.HashSizeInBytes * 2
        && hex.All(Uri.IsHexDigit)
        && CryptographicOperations.FixedTimeEquals(Convert.FromHexString(hex), HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body));
}
