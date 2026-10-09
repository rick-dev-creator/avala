using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;
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
    public async Task AJobPlacedByCapacitySaysWhyWithTheReadingsItComparedAsync()
    {
        var job = bench.Job("Fix the flaky checkout test", JobStatus.Running);
        var choice = Outcomes.Present(FakePreview.ByCapacity("claude-personal", ChoiceReason.MostCapacity, ("claude-work", 0.95, false), ("claude-personal", 0.31, true)).Choice);
        bench.Publish(Bench.OnBoard(job) with { Choice = choice });
        using var section = new AutonomySectionViewModel(bench.Inspected());

        await section.FocusAsync(job.Job, bench);

        Assert.Equal("Chosen by capacity: claude-personal had the most left", section.Reason);
        Assert.Equal(
            [
                new CapacityLine("claude-work", "95% of 5h · holds at 90% · at its limit", false, true),
                new CapacityLine("claude-personal", "31% of 5h · holds at 90%", true, false),
            ],
            section.Compared);
    }

    [Fact]
    public async Task AJobPlacedWhileEveryConnectionWasAtItsLimitSaysSoAsync()
    {
        var job = bench.Job("Fix the flaky checkout test", JobStatus.Running);
        var choice = Outcomes.Present(FakePreview.ByCapacity("claude-personal", ChoiceReason.AllAtLimit, ("claude-work", 0.95, false), ("claude-personal", 0.92, false)).Choice);
        bench.Publish(Bench.OnBoard(job) with { Choice = choice });
        using var section = new AutonomySectionViewModel(bench.Inspected());

        await section.FocusAsync(job.Job, bench);

        Assert.Equal("Chosen by capacity: every connection was at its limit, so claude-personal, the least used", section.Reason);
    }

    [Fact]
    public async Task AJobThatHasNotStartedSaysSoOnTheDefaultConnection()
    {
        using var section = new AutonomySectionViewModel(bench.Inspected());

        await section.FocusAsync(bench.Job("Add invoice PDF endpoint", JobStatus.Preparing).Job, bench);

        Assert.Equal(("Not started yet", "The default connection", string.Empty, 0), (section.Autonomy, section.Connection, section.Reason, section.Compared.Count));
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

    [Fact]
    public async Task TheHeaderFactIsTheAutonomyTheJobRunsUnder()
    {
        using var section = new AutonomySectionViewModel(bench.Inspected());
        await section.FocusAsync(InspectedJobs.Reviewed(bench).Job, bench);
        var reviewed = section.Fact;

        await section.FocusAsync(bench.Job("Fix JPY rounding in invoice totals", JobStatus.Preparing).Job, bench);

        Assert.Equal(("Supervised", "not started"), (reviewed, section.Fact));
    }

    public void Dispose() => bench.Dispose();
}
