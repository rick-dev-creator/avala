using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Inspector;

namespace Avala.Workbench.Tests.Inspector;

public sealed class AutonomySectionViewModelScripts : IDisposable
{
    private readonly Bench bench = new();

    [Fact]
    public async Task AJobInFocusShowsHowItsAutonomyWasTightenedAndItsConnection()
    {
        using var section = new AutonomySectionViewModel(bench.Inspected());

        await section.FocusAsync(InspectedJobs.Reviewed(bench).Job, bench);

        Assert.Equal(("Supervised, tightened from the repository's Autonomous", "claude-personal"), (section.Autonomy, section.Connection));
    }

    [Fact]
    public async Task AJobThatHasNotStartedSaysSoOnTheDefaultConnection()
    {
        using var section = new AutonomySectionViewModel(bench.Inspected());

        await section.FocusAsync(bench.Job("Add invoice PDF endpoint", JobStatus.Preparing).Job, bench);

        Assert.Equal(("Not started yet", "The default connection"), (section.Autonomy, section.Connection));
    }

    [Fact]
    public async Task AJobThatAskedForSupervisionBeforeStartingSaysSo()
    {
        var job = bench.Job("Update lodash to 4.17.21", JobStatus.Preparing);
        bench.Catalog.Change(job.Job, history => history with { Summary = history.Summary with { Autonomy = Autonomy.Supervised } });
        using var section = new AutonomySectionViewModel(bench.Inspected());

        await section.FocusAsync(job.Job, bench);

        Assert.Equal("Supervised, as asked", section.Autonomy);
    }

    [Fact]
    public async Task WithNothingInFocusTheSectionIsEmpty()
    {
        using var section = new AutonomySectionViewModel(bench.Inspected());

        await section.FocusAsync(Option<JobId>.None, bench);

        Assert.Equal((string.Empty, string.Empty), (section.Autonomy, section.Connection));
    }

    public void Dispose() => bench.Dispose();
}
