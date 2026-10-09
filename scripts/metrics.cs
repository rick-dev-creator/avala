using System.Globalization;
using System.Text;
using System.Xml.Linq;

var root = Repository.Root().FullName;
var badges = args is ["--badges", var folder] ? Path.GetFullPath(folder) : null;
var lines = await Lines.CountAsync(root);
var quality = await Quality.ReadAsync(
    Path.Combine(root, "TestResults", "metrics", "CodeMetrics.xml"),
    Path.Combine(root, "TestResults", "report", "Cobertura.xml"));
var report = Summary.Of(lines, quality);

Console.WriteLine(report);

if (Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY") is { Length: > 0 } summary)
{
    await File.AppendAllTextAsync(summary, report);
}

if (badges is not null)
{
    await Badges.WriteAsync(badges, lines, quality ?? throw new InvalidOperationException("Badges need the code metrics and the coverage report."));
}

internal static class Lines
{
    public const int T3ProductionLines = 907_000;

    public static async Task<LineCount> CountAsync(string root)
    {
        var production = await CountFolderAsync(Path.Combine(root, "src"));
        var tests = await CountFolderAsync(Path.Combine(root, "tests"));

        return new LineCount(production, tests);
    }

    private static async Task<Count> CountFolderAsync(string directory)
    {
        var files = Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(directory, path)
                .Split(Path.DirectorySeparatorChar)
                .Intersect(["bin", "obj"])
                .Any())
            .ToList();
        var lines = await Task.WhenAll(files.Select(async path => (await File.ReadAllLinesAsync(path)).Length));

        return new Count(files.Count, lines.Sum());
    }
}

internal static class Quality
{
    public const double HotspotCrap = 30;

    public static async Task<Grading?> ReadAsync(string metricsPath, string coveragePath)
    {
        if (!File.Exists(metricsPath) || !File.Exists(coveragePath))
        {
            return null;
        }

        var metrics = await LoadAsync(metricsPath);
        var coverage = await LoadAsync(coveragePath);
        var methods = metrics.Descendants("Method")
            .Where(method => method.Attribute("File")?.Value is { } file && file.StartsWith("src/", StringComparison.Ordinal) && !file.EndsWith(".g.cs", StringComparison.Ordinal))
            .Select(method => new MeasuredMethod(
                method.Attribute("Name")!.Value,
                Metric(method, "MaintainabilityIndex"),
                Metric(method, "CyclomaticComplexity")))
            .ToList();
        var covered = coverage.Descendants("method")
            .Select(method => new Hotspot(
                $"{method.Ancestors("class").First().Attribute("name")!.Value}.{method.Attribute("name")!.Value}",
                Crap(Number(method, "complexity"), Number(method, "line-rate"))))
            .ToList();

        return new Grading(
            Math.Round(100 * Number(coverage.Root!, "line-rate"), 1),
            Math.Round(methods.Average(method => method.Maintainability), 1),
            methods.MaxBy(method => method.Complexity)!,
            covered.Count,
            [.. covered.Where(method => method.Crap > HotspotCrap).OrderByDescending(method => method.Crap)]);
    }

    public static double Crap(double complexity, double coverage) =>
        (complexity * complexity * Math.Pow(1 - coverage, 3)) + complexity;

    private static async Task<XDocument> LoadAsync(string path)
    {
        await using var stream = File.OpenRead(path);

        return await XDocument.LoadAsync(stream, LoadOptions.None, CancellationToken.None);
    }

    private static int Metric(XElement member, string name) =>
        int.Parse(
            member.Element("Metrics")!.Elements("Metric").Single(metric => metric.Attribute("Name")!.Value == name).Attribute("Value")!.Value,
            CultureInfo.InvariantCulture);

    private static double Number(XElement element, string attribute) =>
        double.Parse(element.Attribute(attribute)!.Value, CultureInfo.InvariantCulture);
}

internal static class Ratings
{
    public static char Coverage(double percent) => Rate(percent, [80, 70, 60, 50], higherIsBetter: true);

    public static char Maintainability(double index) => Rate(index, [80, 70, 60, 50], higherIsBetter: true);

    public static char Complexity(int highest) => Rate(highest, [10, 15, 20, 25], higherIsBetter: false);

    public static char Hotspots(double perThousand) => Rate(perThousand, [1, 5, 10, 20], higherIsBetter: false);

    public static char Debt(Grading grading) =>
        new[]
        {
            Coverage(grading.Coverage),
            Maintainability(grading.Maintainability),
            Complexity(grading.MostComplex.Complexity),
            Hotspots(grading.HotspotsPerThousand),
        }.Max();

    private static char Rate(double value, double[] limits, bool higherIsBetter) =>
        (char)('A' + limits.TakeWhile(limit => higherIsBetter ? value < limit : value > limit).Count());
}

internal static class Summary
{
    public static string Of(LineCount lines, Grading? quality) =>
        $"""
        ## C# lines of code

        | Metric | Value |
        | --- | ---: |
        | Production lines | {lines.Production.Lines:N0} |
        | Production files | {lines.Production.Files:N0} |
        | Test lines | {lines.Tests.Lines:N0} |
        | Test files | {lines.Tests.Files:N0} |
        | Test lines per production line | {(double)lines.Tests.Lines / lines.Production.Lines:0.00} |
        | Share of T3 Code's {Lines.T3ProductionLines:N0} production lines | {lines.T3Share:0.000}% |

        {(quality is null ? NotGraded : Graded(quality))}
        """;

    private const string NotGraded = """
        ## Technical debt

        Not graded: run `dotnet run scripts/code-metrics.cs` and the coverage report first, as in docs/architecture.md.

        """;

    private static string Graded(Grading quality) =>
        $"""
        ## Technical debt: grade {Ratings.Debt(quality)}

        | Indicator | Value | Rating |
        | --- | ---: | :---: |
        | Line coverage of the unit tests | {quality.Coverage:0.0}% | {Ratings.Coverage(quality.Coverage)} |
        | Mean maintainability index of the methods | {quality.Maintainability:0.0} | {Ratings.Maintainability(quality.Maintainability)} |
        | Highest cyclomatic complexity of a method | {quality.MostComplex.Complexity} (`{quality.MostComplex.Name}`) | {Ratings.Complexity(quality.MostComplex.Complexity)} |
        | Risk hotspots (CRAP score above {Quality.HotspotCrap:0}) per 1,000 methods | {quality.HotspotsPerThousand:0.0} ({quality.Hotspots.Count} of {quality.Methods:N0}) | {Ratings.Hotspots(quality.HotspotsPerThousand)} |

        {Hotspots(quality)}
        """;

    private static string Hotspots(Grading quality) =>
        quality.Hotspots.Count == 0
            ? "No risk hotspots."
            : new StringBuilder()
                .AppendLine("| Riskiest methods | CRAP score |")
                .AppendLine("| --- | ---: |")
                .AppendJoin(string.Empty, quality.Hotspots.Take(10).Select(hotspot => string.Create(CultureInfo.InvariantCulture, $"| `{hotspot.Name}` | {hotspot.Crap:0} |\n")))
                .ToString();
}

internal static class Badges
{
    private const string Neutral = "#007ec6";

    public static async Task WriteAsync(string folder, LineCount lines, Grading quality)
    {
        Directory.CreateDirectory(folder);
        var debt = Ratings.Debt(quality);

        await Task.WhenAll(
            WriteAsync(folder, "coverage", "coverage", string.Create(CultureInfo.InvariantCulture, $"{quality.Coverage:0.0}%"), Color(Ratings.Coverage(quality.Coverage))),
            WriteAsync(folder, "maintainability", "maintainability", string.Create(CultureInfo.InvariantCulture, $"{quality.Maintainability:0}"), Color(Ratings.Maintainability(quality.Maintainability))),
            WriteAsync(folder, "complexity", "max complexity", string.Create(CultureInfo.InvariantCulture, $"{quality.MostComplex.Complexity}"), Color(Ratings.Complexity(quality.MostComplex.Complexity))),
            WriteAsync(folder, "debt", "debt grade", debt.ToString(), Color(debt)),
            WriteAsync(folder, "lines", "C# lines", Thousands(lines.Production.Lines), Neutral),
            WriteAsync(folder, "t3-share", "vs T3 Code", string.Create(CultureInfo.InvariantCulture, $"{lines.T3Share:0.0}% of 907k lines"), Neutral));
    }

    private static Task WriteAsync(string folder, string name, string label, string value, string color) =>
        File.WriteAllTextAsync(Path.Combine(folder, $"{name}.svg"), Svg.Badge(label, value, color));

    private static string Thousands(int lines) =>
        string.Create(CultureInfo.InvariantCulture, $"{lines / 1000d:0.0}k");

    private static string Color(char rating) => rating switch
    {
        'A' => "#4c1",
        'B' => "#97ca00",
        'C' => "#dfb317",
        'D' => "#fe7d37",
        _ => "#e05d44",
    };
}

internal static class Svg
{
    private static readonly XNamespace Ns = "http://www.w3.org/2000/svg";

    public static string Badge(string label, string value, string color)
    {
        var left = Width(label) + 10;
        var right = Width(value) + 10;
        var width = left + right;

        return new XElement(
            Ns + "svg",
            new XAttribute("width", width),
            new XAttribute("height", 20),
            new XAttribute("role", "img"),
            new XAttribute("aria-label", $"{label}: {value}"),
            new XElement(Ns + "title", $"{label}: {value}"),
            new XElement(
                Ns + "linearGradient",
                new XAttribute("id", "s"),
                new XAttribute("x2", 0),
                new XAttribute("y2", "100%"),
                new XElement(Ns + "stop", new XAttribute("offset", 0), new XAttribute("stop-color", "#bbb"), new XAttribute("stop-opacity", ".1")),
                new XElement(Ns + "stop", new XAttribute("offset", 1), new XAttribute("stop-opacity", ".1"))),
            new XElement(Ns + "clipPath", new XAttribute("id", "r"), Rect(0, width, "#fff", rounded: true)),
            new XElement(
                Ns + "g",
                new XAttribute("clip-path", "url(#r)"),
                Rect(0, left, "#555", rounded: false),
                Rect(left, right, color, rounded: false),
                Rect(0, width, "url(#s)", rounded: false)),
            new XElement(
                Ns + "g",
                new XAttribute("fill", "#fff"),
                new XAttribute("text-anchor", "middle"),
                new XAttribute("font-family", "Verdana,Geneva,DejaVu Sans,sans-serif"),
                new XAttribute("text-rendering", "geometricPrecision"),
                new XAttribute("font-size", 110),
                Text(label, left / 2d, left - 10),
                Text(value, left + (right / 2d), right - 10))).ToString(SaveOptions.DisableFormatting);
    }

    private static XElement Rect(int x, int width, string fill, bool rounded) =>
        new(
            Ns + "rect",
            new XAttribute("x", x),
            new XAttribute("width", width),
            new XAttribute("height", 20),
            new XAttribute("fill", fill),
            rounded ? new XAttribute("rx", 3) : null);

    private static IEnumerable<XElement> Text(string text, double center, int length) =>
    [
        Text(text, center, length, 150, shadow: true),
        Text(text, center, length, 140, shadow: false),
    ];

    private static XElement Text(string text, double center, int length, int y, bool shadow) =>
        new(
            Ns + "text",
            new XAttribute("x", (int)Math.Round(center * 10)),
            new XAttribute("y", y),
            new XAttribute("transform", "scale(.1)"),
            new XAttribute("textLength", length * 10),
            shadow ? new[] { new XAttribute("aria-hidden", "true"), new XAttribute("fill", "#010101"), new XAttribute("fill-opacity", ".3") } : [],
            text);

    private static int Width(string text) =>
        (int)Math.Ceiling(text.Sum(character => character switch
        {
            'i' or 'l' or 'j' or '.' or ',' or ':' or ';' or '\'' or '!' or '|' => 3.2,
            'f' or 't' or 'r' or 'I' or ' ' or '1' => 4.4,
            'm' or 'w' or 'M' or 'W' or '%' => 10.2,
            >= 'A' and <= 'Z' => 7.6,
            >= '0' and <= '9' => 7.0,
            '#' => 9.0,
            _ => 6.6,
        }));
}

internal static class Repository
{
    public static DirectoryInfo Root() => Find(new DirectoryInfo(Directory.GetCurrentDirectory()));

    private static DirectoryInfo Find(DirectoryInfo directory) =>
        File.Exists(Path.Combine(directory.FullName, "Avala.slnx"))
            ? directory
            : Find(directory.Parent ?? throw new InvalidOperationException("Run the script inside the repository."));
}

internal sealed record Count(int Files, int Lines);

internal sealed record LineCount(Count Production, Count Tests)
{
    public double T3Share => 100d * Production.Lines / Lines.T3ProductionLines;
}

internal sealed record MeasuredMethod(string Name, int Maintainability, int Complexity);

internal sealed record Hotspot(string Name, double Crap);

internal sealed record Grading(double Coverage, double Maintainability, MeasuredMethod MostComplex, int Methods, IReadOnlyList<Hotspot> Hotspots)
{
    public double HotspotsPerThousand => 1000d * Hotspots.Count / Methods;
}
