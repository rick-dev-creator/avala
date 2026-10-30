using System.Xml.Linq;

namespace Avala.Server.ArchitectureTests.Layout;

internal static class BackendLayout
{
    private static readonly string[] BuildOutputFolders = ["bin", "obj"];

    public static DirectoryInfo Root { get; } = FindRoot(new DirectoryInfo(AppContext.BaseDirectory));

    public static string Backend { get; } = Path.Combine(Root.FullName, "backend");

    public static string Modules { get; } = Path.Combine(Backend, "Fleet", "Modules");

    public static string ViolatingFixtures { get; } = Path.Combine(Backend, "tests", "Fixtures", "Violating");

    public static string HostProject { get; } = Path.Combine(Backend, "Fleet", ServerNames.Host, $"{ServerNames.Host}.csproj");

    public static IEnumerable<string> FilesUnder(string directory, string pattern) =>
        Directory.EnumerateFiles(directory, pattern, SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(directory, path)
                .Split(Path.DirectorySeparatorChar)
                .Intersect(BuildOutputFolders)
                .Any());

    public static IReadOnlyList<string> ProjectNamesUnder(string directory) =>
        [.. FilesUnder(directory, "*.csproj").Select(Path.GetFileNameWithoutExtension).OfType<string>().Order(StringComparer.Ordinal)];

    public static async Task<IReadOnlyList<string>> ProjectReferencesAsync(string projectPath, CancellationToken cancellationToken)
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
