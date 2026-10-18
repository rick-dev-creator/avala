using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workbench.Inspector;

namespace Avala.Workbench.Tests.Inspector;

public sealed class WorktreeSectionViewModelScripts : IDisposable
{
    private readonly Bench bench = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AJobInFocusShowsItsBranchBaseFolderAndPortLease()
    {
        using var section = new WorktreeSectionViewModel(bench.Inspected(), bench.ScratchFolders, bench.Jobs);
        var job = InspectedJobs.Reviewed(bench);

        await section.FocusAsync(job.Job, bench);

        Assert.Equal(
            ("avala/fix-the-test", "main at ba5eba5", $"/worktrees/{job.Job.Value}", "Ports 41000–41009"),
            (section.Branch, section.Base, section.Path.Value, section.Ports));
        Assert.Equal((false, false), (section.IsScratch, section.OpenFolderCommand.CanExecute(null)));
    }

    [Fact]
    public async Task AJobStartedWithoutARepositoryIsLabelledScratchAndOpensItsScratchFolder()
    {
        using var section = new WorktreeSectionViewModel(bench.Inspected(), bench.ScratchFolders, bench.Jobs);
        var job = Scratch();
        await section.FocusAsync(job.Job, bench);

        await section.OpenFolderCommand.ExecuteAsync(null);

        Assert.Equal((true, ScratchFolder, "fix-the-test from main · scratch", string.Empty), (section.IsScratch, section.Repository, section.Fact, section.FolderProblem));
        Assert.Equal([ScratchFolder], bench.Opener.Opened);
    }

    [Fact]
    public async Task AScratchFolderThatCannotBeOpenedSaysWhereItIs()
    {
        using var section = new WorktreeSectionViewModel(bench.Inspected(), bench.ScratchFolders, bench.Jobs);
        bench.Opener.Refusal = FileOpenError.Unavailable;
        await section.FocusAsync(Scratch().Job, bench);

        await section.OpenFolderCommand.ExecuteAsync(null);

        Assert.Equal($"The folder could not be opened here: it is {ScratchFolder}.", section.FolderProblem);
    }

    [Fact]
    public async Task AJobWithoutAWorktreeSaysSo()
    {
        using var section = new WorktreeSectionViewModel(bench.Inspected(), bench.ScratchFolders, bench.Jobs);
        var job = bench.Catalog.Add("Add invoice PDF endpoint", JobStatus.Preparing).Summary;

        await section.FocusAsync(job.Job, bench);

        Assert.Equal(("No worktree", string.Empty, string.Empty), (section.Branch, section.Path.Value, section.Ports));
    }

    [Fact]
    public async Task ALoadForAJobThatLostTheFocusBeforeItArrivedIsNeverShown()
    {
        var first = bench.Job("Rate-limit POST /login", JobStatus.AwaitingReview);
        var second = bench.Job("Fix JPY rounding in invoice totals", JobStatus.Running);
        var hooked = new HookedDispatcher(bench.Ui);
        using var section = new WorktreeSectionViewModel(bench.Inspected(hooked), bench.ScratchFolders, bench.Jobs);
        await bench.Ui.InvokeAsync(section.Activate, Cancellation);
        hooked.BeforeNext = () => section.OnRegionContextChanged(second.Job);

        await ViewModelScript.Given(section).WhenPresentedAsync(_ => bench.Post(() => section.OnRegionContextChanged(first.Job)), Cancellation);
        await bench.Ui.ReadAsync(() => true);

        Assert.Equal($"/worktrees/{second.Job.Value}", await bench.Ui.ReadAsync(() => section.Path.Value));
        Assert.Equal(1L, await bench.Ui.ReadAsync(() => section.Revision));
    }

    [Fact]
    public async Task AJobTheCatalogNoLongerKnowsLeavesTheSectionEmpty()
    {
        using var section = new WorktreeSectionViewModel(bench.Inspected(), bench.ScratchFolders, bench.Jobs);

        await section.FocusAsync(JobId.New(), bench);

        Assert.Equal((false, string.Empty), (section.IsLoaded, section.Branch));
    }

    [Fact]
    public async Task TheHeaderFactIsTheBranchsShortNameAndItsBaseOrNone()
    {
        using var section = new WorktreeSectionViewModel(bench.Inspected(), bench.ScratchFolders, bench.Jobs);
        await section.FocusAsync(InspectedJobs.Reviewed(bench).Job, bench);
        var reviewed = section.Fact;

        await section.FocusAsync(bench.Catalog.Add("Add invoice PDF endpoint").Summary.Job, bench);

        Assert.Equal(("fix-the-test from main", "none"), (reviewed, section.Fact));
    }

    [Fact]
    public async Task AJobThatCouldNotStartSaysWhyAndRetriesItsStart()
    {
        using var section = new WorktreeSectionViewModel(bench.Inspected(), bench.ScratchFolders, bench.Jobs);
        var job = CouldNotStart();
        await section.FocusAsync(job.Job, bench);
        var shown = (section.CouldNotStart, section.Fact, section.StartProblem, section.RetryCommand.CanExecute(null));

        await section.RetryCommand.ExecuteAsync(null);

        Assert.Equal((true, "could not start", Locked, true), shown);
        Assert.Equal(["retry"], bench.Jobs.Calls);
        Assert.Equal(string.Empty, section.RetryRefusal);
    }

    [Fact]
    public async Task ARetryTheJobRefusesSaysWhy()
    {
        using var section = new WorktreeSectionViewModel(bench.Inspected(), bench.ScratchFolders, bench.Jobs);
        bench.Jobs.Refusal = JobRejection.NotHeld;
        await section.FocusAsync(CouldNotStart().Job, bench);

        await section.RetryCommand.ExecuteAsync(null);

        Assert.Equal("The job did not start again. The job takes a message only when it needs you, awaits review or was approved.", section.RetryRefusal);
    }

    [Fact]
    public async Task AJobHeldForAnotherReasonOffersNoRetry()
    {
        using var section = new WorktreeSectionViewModel(bench.Inspected(), bench.ScratchFolders, bench.Jobs);
        var job = bench.Catalog.Add("Add invoice PDF endpoint", JobStatus.NeedsHelp).Summary;
        bench.Catalog.Change(job.Job, history => history with { Summary = history.Summary with { Hold = HoldReason.Stalled, Problem = Locked } });

        await section.FocusAsync(job.Job, bench);

        Assert.Equal((false, string.Empty, false), (section.CouldNotStart, section.StartProblem, section.RetryCommand.CanExecute(null)));
    }

    private const string Locked = "The process cannot access the file because it is being used by another process.";

    private const string ScratchFolder = "/data/scratch/2026-10-12-fix-the-test-a1b2c3";

    private JobSummary CouldNotStart()
    {
        var job = bench.Catalog.Add("Add invoice PDF endpoint", JobStatus.NeedsHelp).Summary;
        bench.Catalog.Change(job.Job, history => history with { Summary = history.Summary with { Hold = HoldReason.CouldNotStart, Problem = Locked } });

        return job;
    }

    private JobSummary Scratch()
    {
        var job = bench.Job("Fix the test", JobStatus.AwaitingReview);
        bench.Catalog.Change(job.Job, history => history with { Summary = history.Summary with { Repository = ScratchFolder } });

        return job;
    }

    public void Dispose() => bench.Dispose();
}
