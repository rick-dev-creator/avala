using System.Xml.Linq;

if (args.Length != 1)
{
    await Console.Error.WriteLineAsync("Usage: dotnet run scripts/coverage-paths.cs -- <folder of Cobertura reports>");
    return 1;
}

var root = Repository.Root().FullName;
var reports = Directory.GetFiles(Path.GetFullPath(args[0]), "*.cobertura.xml", SearchOption.AllDirectories);

foreach (var report in reports)
{
    await Reports.RelativizeAsync(report, root);
}

Console.WriteLine($"Source paths of {reports.Length} coverage reports made relative to {root}");

return 0;

internal static class Reports
{
    public static async Task RelativizeAsync(string report, string root)
    {
        XDocument document;

        await using (var reading = File.OpenRead(report))
        {
            document = await XDocument.LoadAsync(reading, LoadOptions.None, CancellationToken.None);
        }

        foreach (var file in document.Descendants("class").Attributes("filename"))
        {
            file.Value = SourcePaths.Relative(file.Value, root);
        }

        await using var writing = File.Create(report);
        await document.SaveAsync(writing, SaveOptions.DisableFormatting, CancellationToken.None);
    }
}

internal static class SourcePaths
{
    public static string Relative(string path, string root) =>
        Path.IsPathFullyQualified(path) && Path.GetRelativePath(root, path) is var relative && !relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathFullyQualified(relative)
            ? relative.Replace('\\', '/')
            : path;
}

internal static class Repository
{
    public static DirectoryInfo Root() => Find(new DirectoryInfo(Directory.GetCurrentDirectory()));

    private static DirectoryInfo Find(DirectoryInfo directory) =>
        File.Exists(Path.Combine(directory.FullName, "Avala.slnx"))
            ? directory
            : Find(directory.Parent ?? throw new InvalidOperationException("Run the script inside the repository."));
}
