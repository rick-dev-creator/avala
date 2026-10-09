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
        using var section = new WorktreeSectionViewModel(bench.Inspected());
        var job = InspectedJobs.Reviewed(bench);

        await section.FocusAsync(job.Job, bench);

        Assert.Equal(
            ("avala/fix-the-test", "main at ba5eba5", $"/worktrees/{job.Job.Value}", "Ports 41000–41009"),
            (section.Branch, section.Base, section.Path, section.Ports));
    }

    [Fact]
    public async Task AJobWithoutAWorktreeSaysSo()
    {
        using var section = new WorktreeSectionViewModel(bench.Inspected());
        var job = bench.Catalog.Add("Add invoice PDF endpoint", JobStatus.Preparing).Summary;

        await section.FocusAsync(job.Job, bench);

        Assert.Equal(("No worktree", string.Empty, string.Empty), (section.Branch, section.Path, section.Ports));
    }

    [Fact]
    public async Task ALoadForAJobThatLostTheFocusBeforeItArrivedIsNeverShown()
    {
        var first = bench.Job("Rate-limit POST /login", JobStatus.AwaitingReview);
        var second = bench.Job("Fix JPY rounding in invoice totals", JobStatus.Running);
        var hooked = new HookedDispatcher(bench.Ui);
        using var section = new WorktreeSectionViewModel(bench.Inspected(hooked));
        await bench.Ui.InvokeAsync(section.Activate, Cancellation);
        hooked.BeforeNext = () => section.OnRegionContextChanged(second.Job);

        await ViewModelScript.Given(section).WhenPresentedAsync(_ => bench.Post(() => section.OnRegionContextChanged(first.Job)), Cancellation);
        await bench.Ui.ReadAsync(() => true);

        Assert.Equal($"/worktrees/{second.Job.Value}", await bench.Ui.ReadAsync(() => section.Path));
        Assert.Equal(1L, await bench.Ui.ReadAsync(() => section.Revision));
    }

    [Fact]
    public async Task AJobTheCatalogNoLongerKnowsLeavesTheSectionEmpty()
    {
        using var section = new WorktreeSectionViewModel(bench.Inspected());

        await section.FocusAsync(JobId.New(), bench);

        Assert.Equal((false, string.Empty), (section.IsLoaded, section.Branch));
    }

    public void Dispose() => bench.Dispose();
}
