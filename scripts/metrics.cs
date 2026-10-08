const int T3ProductionLines = 907_000;

var root = FindRoot(new DirectoryInfo(Directory.GetCurrentDirectory()));
var production = await CountAsync(Path.Combine(root.FullName, "src"));
var tests = await CountAsync(Path.Combine(root.FullName, "tests"));

var report = $"""
    ## C# lines of code

    | Metric | Value |
    | --- | ---: |
    | Production lines | {production.Lines:N0} |
    | Production files | {production.Files:N0} |
    | Test lines | {tests.Lines:N0} |
    | Test files | {tests.Files:N0} |
    | Test lines per production line | {(double)tests.Lines / production.Lines:0.00} |
    | Share of T3 Code's {T3ProductionLines:N0} production lines | {100d * production.Lines / T3ProductionLines:0.000}% |

    """;

Console.WriteLine(report);

if (Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY") is { Length: > 0 } summary)
{
    await File.AppendAllTextAsync(summary, report);
}

static async Task<Count> CountAsync(string directory)
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

static DirectoryInfo FindRoot(DirectoryInfo directory) =>
    File.Exists(Path.Combine(directory.FullName, "Avala.slnx"))
        ? directory
        : FindRoot(directory.Parent ?? throw new InvalidOperationException("Run the script inside the repository."));

internal sealed record Count(int Files, int Lines);
