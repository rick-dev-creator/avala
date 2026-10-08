using Avala.Testing;
using Avala.Workspaces.Workspaces;

namespace Avala.Workspaces.Tests.Workspaces;

public sealed class WorkspaceLifecycleDiagramTests
{
    [Fact]
    public async Task TheDocumentedDiagramMatchesTheLifecycleAsync()
    {
        var diagram = await StateDiagram.CompareAsync(
            "workspace-lifecycle.md",
            "Workspace lifecycle",
            nameof(WorkspaceLifecycle),
            WorkspaceLifecycle.Create(() => WorkspaceState.Creating, _ => { }).GetInfo(),
            TestContext.Current.CancellationToken);

        Assert.Equal(diagram.Expected, diagram.Documented);
    }
}
