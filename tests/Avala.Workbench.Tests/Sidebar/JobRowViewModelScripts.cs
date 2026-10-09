using Avala.Components.Status;
using Avala.Jobs.Contracts;
using Avala.Testing;
using Avala.Workbench.Board;
using Avala.Workbench.Sidebar;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Tests.Sidebar;

public sealed class JobRowViewModelScripts
{
    [Fact]
    public void ARowShowsTheFirstLineOfTheInstructionAndOneFact() =>
        ViewModelScript.Given(new JobRowViewModel(Job("Fix JPY rounding in invoice totals\nYen has no minor unit", JobStatus.Running)))
            .Then(row => Assert.Equal(("Fix JPY rounding in invoice totals", "working", 0, false, StatusKind.Working), (row.Title, row.Fact, row.PendingDecisions, row.HasPendingDecisions, row.Dot.Kind)));

    [Fact]
    public void AnUpdateChangesTheFactTheGroupAndTheDotInPlace()
    {
        var job = Job("Rate-limit POST /login", JobStatus.Checking);

        ViewModelScript.Given(new JobRowViewModel(job))
            .When(row => row.Update(job with { Summary = job.Summary with { Status = JobStatus.AwaitingReview } }))
            .ThenNotified(nameof(JobRowViewModel.Status), nameof(JobRowViewModel.Group), nameof(JobRowViewModel.Fact))
            .Then(row => Assert.Equal((JobGroup.ReadyForReview, "ready for review", StatusKind.ReadyForReview), (row.Group, row.Fact, row.Dot.Kind)));
    }

    [Fact]
    public void ARowIsNotSelectedUntilTheSidebarSelectsIt() =>
        ViewModelScript.Given(new JobRowViewModel(Job("Add invoice PDF endpoint", JobStatus.Preparing)))
            .Then(row => Assert.Equal((false, "starting"), (row.IsSelected, row.Fact)))
            .When(row => row.IsSelected = true)
            .ThenNotified(nameof(JobRowViewModel.IsSelected));

    private static BoardJob Job(string instruction, JobStatus status) => new(new FakeCatalog().Add(instruction, status).Summary, Transcript.Empty);
}
