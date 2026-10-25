using System.Text.RegularExpressions;

namespace Avala.ArchitectureTests.Source;

internal static partial class ScriptLanguages
{
    private static readonly string[] ForeignExtensions =
        [".py", ".sh", ".bash", ".zsh", ".ps1", ".psm1", ".bat", ".cmd", ".js", ".mjs", ".cjs", ".ts", ".rb", ".pl", ".lua", ".php"];

    public const string SiteTemplate = "src/Modules/Documentation/Avala.Documentation/SiteTemplate/public/";

    public const string LaidOutSiteTemplate = "docs/template/public/";

    public const string ModelViewer = "src/Modules/Preview/Avala.Preview/Viewer/";

    public static IEnumerable<string> ForeignScripts(IEnumerable<string> paths) =>
        paths.Where(path => ForeignExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)
            && !InFolder(path, SiteTemplate) && !InFolder(path, LaidOutSiteTemplate) && !UnderFolder(path, ModelViewer));

    private static bool UnderFolder(string path, string folder)
    {
        var normalized = path.Replace('\\', '/');

        return Path.GetExtension(normalized).Equals(".js", StringComparison.Ordinal)
            && (normalized.StartsWith(folder, StringComparison.Ordinal) || normalized.Contains("/" + folder, StringComparison.Ordinal));
    }

    private static bool InFolder(string path, string folder)
    {
        var normalized = path.Replace('\\', '/');

        return Path.GetExtension(normalized).Equals(".js", StringComparison.Ordinal)
            && (normalized.StartsWith(folder, StringComparison.Ordinal) || normalized.Contains("/" + folder, StringComparison.Ordinal))
            && normalized[(normalized.IndexOf(folder, StringComparison.Ordinal) + folder.Length)..].IndexOf('/', StringComparison.Ordinal) < 0;
    }

    public static IEnumerable<string> NonDotnetCommands(string workflow)
    {
        var lines = workflow.ReplaceLineEndings("\n").Split('\n');

        return lines
            .Select((line, index) => (Match: RunStep().Match(line), Index: index))
            .Where(step => step.Match.Success)
            .Select(step => Command(lines, step.Index, step.Match))
            .Where(command => !command.StartsWith("dotnet ", StringComparison.Ordinal));
    }

    private static string Command(string[] lines, int index, Match step)
    {
        var value = step.Groups["value"].Value.Trim();

        if (value is not (">-" or ">" or "|" or "|-"))
        {
            return value;
        }

        var indent = step.Groups["indent"].Value.Length;

        return string.Join(' ', lines
            .Skip(index + 1)
            .TakeWhile(line => line.Trim().Length == 0 || line.Length - line.TrimStart().Length > indent)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0));
    }

    [GeneratedRegex(@"^(?<indent>\s*)(?:-\s+)?run:\s*(?<value>.*)$")]
    private static partial Regex RunStep();
}
