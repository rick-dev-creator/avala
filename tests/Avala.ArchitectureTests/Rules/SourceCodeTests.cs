using Avala.ArchitectureTests.Solution;
using Avala.ArchitectureTests.Source;

namespace Avala.ArchitectureTests.Rules;

public sealed class SourceCodeTests
{
    private const int MaximumTypeLines = 600;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static IEnumerable<string> CSharpFiles =>
    [
        .. SolutionLayout.FilesUnder(SolutionLayout.SourceDirectory, "*.cs"),
        .. SolutionLayout.FilesUnder(SolutionLayout.TestsDirectory, "*.cs"),
        .. SolutionLayout.FilesUnder(Path.Combine(SolutionLayout.Root.FullName, "scripts"), "*.cs"),
    ];

    [Fact]
    public async Task CSharpCodeHasNoCommentsAsync()
    {
        var files = await SourceFile.ReadAllAsync(CSharpFiles, Cancellation);

        var violations = files.SelectMany(file =>
            CommentFinder.InCSharp(file.Text).Select(line => $"{file.Path}:{line}"));

        Assert.Empty(violations);
    }

    [Fact]
    public async Task XamlHasNoCommentsAsync()
    {
        var files = await SourceFile.ReadAllAsync(
            SolutionLayout.FilesUnder(SolutionLayout.SourceDirectory, "*.axaml"),
            Cancellation);

        var violations = files.SelectMany(file =>
            CommentFinder.InXaml(file.Text).Select(line => $"{file.Path}:{line}"));

        Assert.Empty(violations);
    }

    [Fact]
    public void TheRepositoryHoldsNoScriptsInOtherLanguages() =>
        Assert.Empty(ScriptLanguages.ForeignScripts(SolutionLayout.FilesUnder(SolutionLayout.Root.FullName, "*")));

    [Fact]
    public async Task WorkflowsOnlyRunDotnetAsync()
    {
        var workflows = await SourceFile.ReadAllAsync(
            SolutionLayout.FilesUnder(Path.Combine(SolutionLayout.Root.FullName, ".github", "workflows"), "*.yml"),
            Cancellation);

        Assert.Empty(workflows.SelectMany(workflow =>
            ScriptLanguages.NonDotnetCommands(workflow.Text).Select(command => $"{workflow.Path}: {command}")));
    }

    [Fact]
    public async Task NoTypeExceedsTheLineLimitAsync()
    {
        var files = await SourceFile.ReadAllAsync(CSharpFiles, Cancellation);

        var violations = TypeSizeMeter.Measure(files.Select(file => file.Text))
            .Where(type => type.Value > MaximumTypeLines)
            .Select(type => $"{type.Key}: {type.Value} lines");

        Assert.Empty(violations);
    }
}
