using System.Security.Cryptography;
using System.Text;

namespace Avala.Sdk.Processes;

public static class ScratchFolders
{
    public const string Root = "temp";

    public static IReadOnlyList<string> Variables { get; } = ["TMPDIR", "TEMP", "TMP"];

    public static string Of(AvalaPaths paths, string home) => Path.Combine(paths.Folder(Root), Key(home));

    private static string Key(string home)
    {
        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(home));
        var spelled = OperatingSystem.IsWindows() ? normalized.ToUpperInvariant() : normalized;

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(spelled)))[..16];
    }
}
