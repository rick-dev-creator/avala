namespace Avala.Permissions.Governance;

internal static class SystemTemporaryFolders
{
    public static IReadOnlyList<string> All { get; } =
        [.. new[] { Path.GetTempPath() }.Concat(OperatingSystem.IsWindows() ? [] : ["/tmp", "/var/tmp"]).Select(Path.TrimEndingDirectorySeparator).Distinct(StringComparer.Ordinal)];
}
