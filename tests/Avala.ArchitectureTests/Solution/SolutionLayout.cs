using System.Xml.Linq;

namespace Avala.ArchitectureTests.Solution;

internal static class SolutionLayout
{
    private static readonly string[] BuildOutputFolders = ["bin", "obj"];

    public static DirectoryInfo Root { get; } = FindRoot(new DirectoryInfo(AppContext.BaseDirectory));

    public static string SourceDirectory { get; } = Path.Combine(Root.FullName, "src");

    public static string TestsDirectory { get; } = Path.Combine(Root.FullName, "tests");

    public static IReadOnlyList<SourceProject> SourceProjects { get; } =
        [.. FilesUnder(SourceDirectory, "*.csproj")
            .Select(path => SourceProject.From(SourceDirectory, path))
            .OrderBy(project => project.Name, StringComparer.Ordinal)];

    public static IEnumerable<SourceProject> ModuleProjects =>
        SourceProjects.Where(project => project.Module is not null);

    public static SourceProject Project(string name) =>
        SourceProjects.Single(project => project.Name == name);

    public static IEnumerable<string> FilesUnder(string directory, string pattern) =>
        Directory.EnumerateFiles(directory, pattern, SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(directory, path)
                .Split(Path.DirectorySeparatorChar)
                .Intersect(BuildOutputFolders)
                .Any());

    public static async Task<IReadOnlyList<string>> ProjectReferencesAsync(
        string projectPath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(projectPath, new FileStreamOptions { Options = FileOptions.Asynchronous });
        var document = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);

        return
        [
            .. document.Descendants("ProjectReference")
                .Select(reference => (string?)reference.Attribute("Include") ?? string.Empty)
                .Select(include => Path.GetFileNameWithoutExtension(include.Replace('\\', '/'))),
        ];
    }

    private static DirectoryInfo FindRoot(DirectoryInfo directory) =>
        File.Exists(Path.Combine(directory.FullName, "Avala.slnx"))
            ? directory
            : FindRoot(directory.Parent ?? throw new InvalidOperationException("Avala.slnx not found above the test output."));
}
