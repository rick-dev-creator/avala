namespace Avala.ClaudeCode.Protocol;

internal sealed record Places(string WorkingDirectory, string Plans)
{
    public const string PlansFolder = "plans";

    private const string PlanExtension = ".md";

    private static readonly char[] Separators = ['/', '\\'];

    private static StringComparison Comparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public bool HoldsPlan(string path)
    {
        if (!Path.IsPathFullyQualified(path) || !Path.IsPathFullyQualified(Plans) || path.Contains('\0', StringComparison.Ordinal)
            || path.Split(Separators).Any(part => part is "." or ".."))
        {
            return false;
        }

        var file = Path.GetFullPath(path);
        var name = Path.GetFileName(file);

        return string.Equals(Path.GetDirectoryName(file), Path.TrimEndingDirectorySeparator(Path.GetFullPath(Plans)), Comparison)
            && name.Length > PlanExtension.Length
            && name.EndsWith(PlanExtension, StringComparison.OrdinalIgnoreCase)
            && name.IndexOfAny(Path.GetInvalidFileNameChars().Append(':').ToArray()) < 0
            && !Linked(new DirectoryInfo(Plans))
            && !Linked(new FileInfo(file));
    }

    private static bool Linked(FileSystemInfo entry)
    {
        try
        {
            return entry.LinkTarget is not null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }
}
