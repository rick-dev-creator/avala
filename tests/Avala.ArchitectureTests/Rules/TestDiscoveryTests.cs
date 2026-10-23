using System.Text.Json;
using System.Xml.Linq;
using Avala.ArchitectureTests.Discovery;
using Avala.ArchitectureTests.Solution;

namespace Avala.ArchitectureTests.Rules;

public sealed class TestDiscoveryTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TestAttributesNameOnlyTypesTheirTestAssemblyShipsAsync()
    {
        var host = Assert.Single(TestAssembly.All, assembly => assembly.Name == "Avala.Host.Tests");
        Assert.False(host.Ships("Avala.Jobs.Contracts"), "The host tests load the module contracts from the plugin folder.");

        var violations = (await Task.WhenAll(TestAssembly.All.Select(assembly => assembly.UnshippedAttributeTypesAsync(Cancellation))))
            .SelectMany(found => found)
            .ToList();

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public async Task EveryTestProjectRunsInExactlyOneSolutionFilterAsync()
    {
        string[] filters = ["Avala.UnitTests.slnf", "Avala.SystemTests.slnf"];
        var listed = await Task.WhenAll(filters.Select(FilteredProjectsAsync));
        var tests = await TestProjectsAsync();

        var violations = tests
            .Select(project => (Project: project, Filters: filters.Where((_, index) => listed[index].Contains(project)).ToList()))
            .Where(found => found.Filters.Count != 1)
            .Select(found => $"{found.Project} runs in {found.Filters.Count} solution filters, not exactly one of {string.Join(" and ", filters)}.")
            .ToList();

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    private static async Task<IReadOnlyList<string>> TestProjectsAsync()
    {
        await using var stream = File.OpenRead(Path.Combine(SolutionLayout.Root.FullName, "Avala.slnx"));
        var solution = await XDocument.LoadAsync(stream, LoadOptions.None, Cancellation);

        return
        [
            .. solution.Descendants("Project")
                .Select(project => (string?)project.Attribute("Path") ?? string.Empty)
                .Where(path => path.StartsWith("tests/", StringComparison.Ordinal) && path.EndsWith("Tests.csproj", StringComparison.Ordinal)),
        ];
    }

    private static async Task<HashSet<string>> FilteredProjectsAsync(string filter)
    {
        await using var stream = File.OpenRead(Path.Combine(SolutionLayout.Root.FullName, filter));
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: Cancellation);

        return
        [
            .. document.RootElement.GetProperty("solution").GetProperty("projects").EnumerateArray()
                .Select(project => project.GetString() ?? string.Empty),
        ];
    }
}
