using Avala.Jobs.Domain;
using Stateless.Graph;

namespace Avala.Jobs.Tests.Domain;

public sealed class JobLifecycleDiagramTests
{
    private static readonly string DiagramPath = Path.Combine(RepositoryRoot().FullName, "docs", "diagrams", "job-lifecycle.md");

    [Fact]
    public async Task TheDocumentedDiagramMatchesTheLifecycleAsync()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var diagram = Render();

        if (Environment.GetEnvironmentVariable("AVALA_UPDATE_DIAGRAMS") == "1")
        {
            await File.WriteAllTextAsync(DiagramPath, diagram, cancellation);
        }

        Assert.Equal(diagram, await File.ReadAllTextAsync(DiagramPath, cancellation));
    }

    private static string Render() =>
        $"""
        # Job lifecycle

        Generated from `JobLifecycle`. Run the tests with `AVALA_UPDATE_DIAGRAMS=1` to refresh it.

        ```mermaid
        {MermaidGraph.Format(JobLifecycle.Create(() => JobState.Draft, _ => { }, () => true).GetInfo()).Trim()}
        ```

        """;

    private static DirectoryInfo RepositoryRoot(DirectoryInfo? directory = null)
    {
        var current = directory ?? new DirectoryInfo(AppContext.BaseDirectory);

        return File.Exists(Path.Combine(current.FullName, "Avala.slnx"))
            ? current
            : RepositoryRoot(current.Parent ?? throw new InvalidOperationException("Avala.slnx not found"));
    }
}
