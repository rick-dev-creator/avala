using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Avala.Server.Instances.Domain;

internal static class InstanceKey
{
    public const string Prefix = "avk_";

    public const string Masked = Prefix + "…";

    public const int HashLength = 64;

    private const int SecretBytes = 32;

    public static string Issue() => Prefix + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(SecretBytes));

    public static string Hash(string key) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
}
