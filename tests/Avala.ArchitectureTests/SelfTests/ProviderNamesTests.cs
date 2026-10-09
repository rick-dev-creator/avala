using Avala.ArchitectureTests.Source;

namespace Avala.ArchitectureTests.SelfTests;

public sealed class ProviderNamesTests
{
    private static readonly string Source = Path.Combine(Path.GetTempPath(), "src");

    [Fact]
    public void FlagsAProviderNameOutsideItsPlugin() =>
        Assert.Equal(
            [$"{Path.Combine(Source, "Modules", "Jobs", "Launch.cs")}: CLAUDE_CONFIG_DIR"],
            ProviderNames.Leaks(Source, [new SourceFile(Path.Combine(Source, "Modules", "Jobs", "Launch.cs"), "var folder = \"CLAUDE_CONFIG_DIR\";")]));

    [Fact]
    public void AcceptsTheNamesInsideTheirPluginAndInDesignTimeData() =>
        Assert.Empty(ProviderNames.Leaks(
            Source,
            [
                new SourceFile(Path.Combine(Source, "Modules", "ClaudeCode", "Avala.ClaudeCode", "CommandLine.cs"), "\"--output-format\", \"stream-json\""),
                new SourceFile(Path.Combine(Source, "Modules", "Workbench", "Avala.Workbench", "DesignViewModels.cs"), "Provider => \"claude-code\""),
            ]));
}
