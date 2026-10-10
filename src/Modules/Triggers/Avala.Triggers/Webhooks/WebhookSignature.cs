using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Avala.Triggers.Contracts;

namespace Avala.Triggers.Webhooks;

internal static class WebhookSignature
{
    public const string TimestampHeader = "X-Avala-Timestamp";

    public const string NonceHeader = "X-Avala-Nonce";

    public const string SignatureHeader = "X-Avala-Signature";

    public const int LongestNonce = 128;

    private const string Scheme = "sha256=";

    public static string Of(string secret, string timestamp, string nonce, ReadOnlySpan<byte> body) =>
        Scheme + Convert.ToHexStringLower(Mac(secret, timestamp, nonce, body));

    public static bool Verifies(string secret, string timestamp, string nonce, ReadOnlySpan<byte> body, string signature)
    {
        if (!signature.StartsWith(Scheme, StringComparison.Ordinal))
        {
            return false;
        }

        var hex = signature[Scheme.Length..];

        if (hex.Length != SHA256.HashSizeInBytes * 2 || !hex.All(Uri.IsHexDigit))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(hex), Mac(secret, timestamp, nonce, body));
    }

    public static bool WellFormedNonce(string nonce) =>
        nonce.Length is > 0 and <= LongestNonce && nonce.All(character => character is > ' ' and <= '~');

    public static bool TryTimestamp(string text, out DateTimeOffset at)
    {
        var parsed = long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            && seconds <= DateTimeOffset.MaxValue.ToUnixTimeSeconds();
        at = parsed ? DateTimeOffset.FromUnixTimeSeconds(seconds) : DateTimeOffset.MinValue;

        return parsed;
    }

    public static string Digest(ReadOnlySpan<byte> body) => Convert.ToHexStringLower(SHA256.HashData(body));

    public static int StatusOf(DeliveryVerdict verdict) =>
        verdict switch
        {
            DeliveryVerdict.Accepted => 202,
            DeliveryVerdict.NotPost => 405,
            DeliveryVerdict.UnknownTrigger => 404,
            DeliveryVerdict.TooLarge => 413,
            DeliveryVerdict.RateLimited => 429,
            DeliveryVerdict.NotSigned or DeliveryVerdict.BadSignature or DeliveryVerdict.Stale => 401,
            DeliveryVerdict.NoSecret => 503,
            DeliveryVerdict.Replayed or DeliveryVerdict.Disabled => 409,
            _ => 400,
        };

    private static byte[] Mac(string secret, string timestamp, string nonce, ReadOnlySpan<byte> body)
    {
        var prefix = Encoding.UTF8.GetBytes($"{timestamp}.{nonce}.");
        var message = new byte[prefix.Length + body.Length];
        prefix.CopyTo(message, 0);
        body.CopyTo(message.AsSpan(prefix.Length));

        return HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), message);
    }
}
