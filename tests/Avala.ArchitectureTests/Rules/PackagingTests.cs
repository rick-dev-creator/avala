using System.Xml.Linq;
using Avala.ArchitectureTests.Solution;

namespace Avala.ArchitectureTests.Rules;

public sealed class PackagingTests
{
    private const string Plugin = "AvalaPlugin";

    private const string DevelopmentOnly = "AvalaDevelopmentOnly";

    private static readonly string[] DevelopmentPlugins = ["Avala.ForgeSimulator", "Avala.Recording", "Avala.Simulator"];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheProductionPluginSetLeavesOutTheDevelopmentOnlyPluginsAsync()
    {
        var plugins = new List<(string Name, bool DevelopmentOnly)>();

        foreach (var project in SolutionLayout.ModuleProjects)
        {
            var document = await LoadAsync(project.Path);

            if (Declares(document, Plugin))
            {
                plugins.Add((project.Name, Declares(document, DevelopmentOnly)));
            }
        }

        var script = await File.ReadAllTextAsync(Path.Combine(SolutionLayout.Root.FullName, "scripts", "package.cs"), Cancellation);
        string[] developmentOnly = [.. plugins.Where(plugin => plugin.DevelopmentOnly).Select(plugin => plugin.Name).Order(StringComparer.Ordinal)];

        Assert.True(
            developmentOnly.SequenceEqual(DevelopmentPlugins),
            $"The development-only plugins are {string.Join(", ", developmentOnly)} instead of {string.Join(", ", DevelopmentPlugins)}. The simulators and the recorder never ship: declare <{DevelopmentOnly}>true</{DevelopmentOnly}> on them, and list a new one here and in docs/release.md.");
        Assert.Contains(plugins, plugin => plugin is { Name: "Avala.ClaudeCode", DevelopmentOnly: false });
        Assert.Contains($"\"{DevelopmentOnly}\"", script, StringComparison.Ordinal);
    }

    private static async Task<XDocument> LoadAsync(string path)
    {
        await using var stream = new FileStream(path, new FileStreamOptions { Options = FileOptions.Asynchronous });

        return await XDocument.LoadAsync(stream, LoadOptions.None, Cancellation);
    }

    private static bool Declares(XDocument project, string property) =>
        project.Descendants(property).Any(element => string.Equals(element.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase));
}
