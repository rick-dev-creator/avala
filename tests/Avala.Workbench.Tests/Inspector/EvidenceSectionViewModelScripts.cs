using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workbench.Inspector;

namespace Avala.Workbench.Tests.Inspector;

public sealed class EvidenceSectionViewModelScripts : IDisposable
{
    private readonly Bench bench = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AJobInFocusShowsItsVerdictAndOneLinePerVerifiedAttempt()
    {
        using var section = new EvidenceSectionViewModel(bench.Inspected());
        var job = InspectedJobs.Reviewed(bench);

        await section.FocusAsync(job.Job, bench);

        Assert.True(section.IsLoaded);
        Assert.Equal("Verified on attempt 2 of 2", section.Summary);
        Assert.Equal(["Attempt 1: failed · tests failed (exit 1)", "Attempt 2: passed · tests passed (exit 0)"], section.Attempts);
    }

    [Fact]
    public async Task LosingTheFocusEmptiesTheSection()
    {
        using var section = new EvidenceSectionViewModel(bench.Inspected());
        await section.FocusAsync(InspectedJobs.Reviewed(bench).Job, bench);

        await section.FocusAsync(Option<JobId>.None, bench);

        Assert.Equal((false, string.Empty, 0), (section.IsLoaded, section.Summary, section.Attempts.Count));
    }

    [Fact]
    public async Task AJobWithoutVerificationsIsNotVerified()
    {
        using var section = new EvidenceSectionViewModel(bench.Inspected());

        await section.FocusAsync(bench.Job("Fix JPY rounding in invoice totals", JobStatus.Running).Job, bench);

        Assert.Equal(("Not verified", 0), (section.Summary, section.Attempts.Count));
    }

    [Fact]
    public async Task ASectionReloadsWhenItsJobsRevisionMoves()
    {
        using var section = new EvidenceSectionViewModel(bench.Inspected());
        var job = bench.Job("Fix JPY rounding in invoice totals", JobStatus.Checking);
        bench.Publish(Bench.OnBoard(job));
        await section.FocusAsync(job.Job, bench);
        bench.Audit.Reports.Add(new Verification.Contracts.VerificationReport(job.Job, 1, Verification.Contracts.VerificationOutcome.Passed, Option<Workspaces.Contracts.FileOrigin>.None, [], GateVerdict.Pass, DateTimeOffset.UnixEpoch));

        await ViewModelScript.Given(section).WhenPresentedAsync(_ => bench.Publish(Bench.OnBoard(job, revision: 1)), Cancellation);

        Assert.Equal("Verified on attempt 1 of 1", section.Summary);
    }

    [Fact]
    public async Task AnInactiveSectionLoadsNothingUntilActivated()
    {
        using var section = new EvidenceSectionViewModel(bench.Inspected());
        var job = InspectedJobs.Reviewed(bench);
        bench.Publish(Bench.OnBoard(job));
        await bench.Ui.InvokeAsync(() => section.OnRegionContextChanged(job.Job), Cancellation);
        var idle = (section.IsLoaded, section.Revision);

        await ViewModelScript.Given(section).WhenPresentedAsync(_ => bench.Post(section.Activate), Cancellation);

        Assert.Equal((false, 0L), idle);
        Assert.True(section.IsLoaded);
    }

    [Fact]
    public async Task ADeactivatedSectionDropsTheLoadItQueuedForTheUi()
    {
        var hooked = new HookedDispatcher(bench.Ui);
        using var section = new EvidenceSectionViewModel(bench.Inspected(hooked));
        await bench.Ui.InvokeAsync(section.Activate, Cancellation);
        hooked.BeforeNext = section.Deactivate;

        await bench.Ui.InvokeAsync(() => section.OnRegionContextChanged(InspectedJobs.Reviewed(bench).Job), Cancellation);
        await (await bench.Ui.ReadAsync(() => section.Loading));

        Assert.Equal((true, false), (hooked.Hooked, await bench.Ui.ReadAsync(() => section.IsLoaded)));
    }

    public void Dispose() => bench.Dispose();
}
