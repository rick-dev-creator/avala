#:property PublishAot=false
#:package Microsoft.CodeAnalysis.Workspaces.MSBuild
#:package Microsoft.CodeAnalysis.CSharp.Workspaces
#:package Microsoft.CodeAnalysis.AnalyzerUtilities

using System.Globalization;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeMetrics;
using Microsoft.CodeAnalysis.MSBuild;

var root = Repository.Root().FullName;
var output = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine(root, "TestResults", "metrics", "CodeMetrics.xml"));
var targets = await Measurement.ProductionAsync(root);

Directory.CreateDirectory(Path.GetDirectoryName(output)!);
await using (var stream = File.Create(output))
{
    await new XDocument(new XElement("CodeMetricsReport", new XAttribute("Version", "1.0"), new XElement("Targets", targets)))
        .SaveAsync(stream, SaveOptions.None, CancellationToken.None);
}

Console.WriteLine($"Code metrics of {targets.Count} projects written to {Path.GetRelativePath(root, output)}");

internal static class Measurement
{
    public static async Task<IReadOnlyList<XElement>> ProductionAsync(string root)
    {
        var sources = Path.Combine(root, "src") + Path.DirectorySeparatorChar;
        using var workspace = MSBuildWorkspace.Create();
        using var failures = workspace.RegisterWorkspaceFailedHandler(failure => Console.Error.WriteLine(failure.Diagnostic.Message));
        var solution = await workspace.OpenSolutionAsync(Path.Combine(root, "Avala.slnx"));
        var targets = new List<XElement>();

        foreach (var project in solution.Projects
            .Where(project => project.FilePath is { } path && path.StartsWith(sources, StringComparison.Ordinal))
            .OrderBy(project => project.Name, StringComparer.Ordinal))
        {
            var compilation = await project.GetCompilationAsync() ?? throw new InvalidOperationException($"{project.Name} has no compilation.");
            var metrics = await CodeAnalysisMetricData.ComputeAsync(compilation.Assembly, new CodeMetricsAnalysisContext(compilation, CancellationToken.None));
            targets.Add(new XElement("Target", new XAttribute("Name", Path.GetFileName(project.FilePath!)), MetricsXml.Element(metrics, root)));
        }

        return targets;
    }
}

internal static class MetricsXml
{
    public static XElement Element(CodeAnalysisMetricData data, string root) =>
        new(
            Kind(data.Symbol),
            [
                new XAttribute("Name", data.Symbol.ToDisplayString()),
                .. Location(data.Symbol, root),
                new XElement(
                    "Metrics",
                    Metric("MaintainabilityIndex", data.MaintainabilityIndex),
                    Metric("CyclomaticComplexity", data.CyclomaticComplexity),
                    Metric("ClassCoupling", data.CoupledNamedTypes.Count),
                    Metric("DepthOfInheritance", data.DepthOfInheritance ?? 0),
                    Metric("SourceLines", data.SourceLines),
                    Metric("ExecutableLines", data.ExecutableLines)),
                .. data.Children.IsEmpty ? [] : new[] { new XElement(Children(data.Symbol), data.Children.Select(child => Element(child, root))) },
            ]);

    private static XElement Metric(string name, long value) =>
        new("Metric", new XAttribute("Name", name), new XAttribute("Value", value.ToString(CultureInfo.InvariantCulture)));

    private static IEnumerable<XAttribute> Location(ISymbol symbol, string root) =>
        symbol.Kind is SymbolKind.Assembly or SymbolKind.Namespace
            ? []
            : symbol.Locations
                .Where(location => location.IsInSource)
                .Take(1)
                .SelectMany(location => new[]
                {
                    new XAttribute("File", Path.GetRelativePath(root, location.SourceTree!.FilePath).Replace('\\', '/')),
                    new XAttribute("Line", location.GetLineSpan().StartLinePosition.Line + 1),
                });

    private static string Kind(ISymbol symbol) => symbol.Kind switch
    {
        SymbolKind.Assembly => "Assembly",
        SymbolKind.Namespace => "Namespace",
        SymbolKind.NamedType => "NamedType",
        SymbolKind.Method => "Method",
        SymbolKind.Property => "Property",
        SymbolKind.Field => "Field",
        SymbolKind.Event => "Event",
        _ => "Symbol",
    };

    private static string Children(ISymbol symbol) => symbol.Kind switch
    {
        SymbolKind.Assembly => "Namespaces",
        SymbolKind.Namespace => "Types",
        _ => "Members",
    };
}

internal static class Repository
{
    public static DirectoryInfo Root() => Find(new DirectoryInfo(Directory.GetCurrentDirectory()));

    private static DirectoryInfo Find(DirectoryInfo directory) =>
        File.Exists(Path.Combine(directory.FullName, "Avala.slnx"))
            ? directory
            : Find(directory.Parent ?? throw new InvalidOperationException("Run the script inside the repository."));
}
