using Avala.Jobs.Domain;
using Avala.Testing;

namespace Avala.Jobs.Tests.Domain;

public sealed class JobLifecycleDiagramTests
{
    [Fact]
    public async Task TheDocumentedDiagramMatchesTheLifecycleAsync()
    {
        var diagram = await StateDiagram.CompareAsync(
            "job-lifecycle.md",
            "Job lifecycle",
            nameof(JobLifecycle),
            JobLifecycle.Create(() => JobState.Draft, _ => { }, () => true).GetInfo(),
            TestContext.Current.CancellationToken);

        Assert.Equal(diagram.Expected, diagram.Documented);
    }
}
