using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Verification.Contracts;
using Avala.Workbench.Inspector;
using Avala.Workbench.Tests.Readiness;

namespace Avala.Workbench.Tests.Inspector;

public sealed class EvidenceSectionViewModelScripts : IDisposable
{
    private readonly Bench bench = new();
    private readonly FakeReadinessLinks links = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AJobInFocusShowsItsVerdictAndOneLinePerVerifiedAttemptWithEachChecksExitAndDuration()
    {
        using var section = new EvidenceSectionViewModel(bench.Inspected(), links);
        var job = InspectedJobs.Reviewed(bench);

        await section.FocusAsync(job.Job, bench);

        Assert.True(section.IsLoaded);
        Assert.Equal("Verified on attempt 2 of 2", section.Summary);
        Assert.Equal(["Attempt 1: failed · tests failed (exit 1, 1 s)", "Attempt 2: passed · tests passed (exit 0, 1 s)"], section.Attempts);
        Assert.Equal((string.Empty, false, string.Empty, 0), (section.Lacks, section.DeclareChecksCommand.CanExecute(null), section.ReportedTitle, section.Reported.Count));
    }

    [Fact]
    public async Task AJobWithoutChecksListsWhatTheAgentRanAfterItsLastEditAndOffersToDeclareChecksForItsRepository()
    {
        using var section = new EvidenceSectionViewModel(bench.Inspected(), links);
        var job = bench.Job("Fix JPY rounding in invoice totals", JobStatus.AwaitingReview);
        bench.Audit.Reports.Add(new VerificationReport(job.Job, 1, VerificationOutcome.NoChecksDeclared, Option<Workspaces.Contracts.FileOrigin>.None, [], GateVerdict.Pass, DateTimeOffset.UnixEpoch));
        bench.Publish(Bench.OnBoard(job) with
        {
            Transcript = Avala.Workbench.Timeline.Transcript.Empty
                .Kept(Tool("edit", ItemKind.FileEdit, "src/Rounding.cs", ItemOutcome.Succeeded))
                .Kept(Tool("build", ItemKind.Command, "dotnet build", ItemOutcome.Succeeded))
                .Kept(Tool("test", ItemKind.Command, "dotnet test", ItemOutcome.Failed)),
        });

        await section.FocusAsync(job.Job, bench);
        await await bench.Ui.ReadAsync(() => section.DeclareChecksCommand.ExecuteAsync(null));

        Assert.Equal(("No checks declared: nothing proves the work", "Reported by the agent after its last edit"), (section.Summary, section.ReportedTitle));
        Assert.Equal(["dotnet build · passed", "dotnet test · failed"], section.Reported);
        Assert.StartsWith("This repository has no .avala/checks.json", section.Lacks, StringComparison.Ordinal);
        Assert.Equal(["/repo"], links.Declared);
    }

    [Fact]
    public async Task AChecksFileThatDeclaresNoChecksIsNotSaidToBeMissing()
    {
        using var section = new EvidenceSectionViewModel(bench.Inspected(), links);
        var job = bench.Job("Fix JPY rounding in invoice totals", JobStatus.AwaitingReview);
        bench.Audit.Reports.Add(new VerificationReport(job.Job, 1, VerificationOutcome.NoChecksDeclared, new Workspaces.Contracts.FileOrigin("abc1234", EditedInWorktree: false), [], GateVerdict.Pass, DateTimeOffset.UnixEpoch));
        bench.Publish(Bench.OnBoard(job));

        await section.FocusAsync(job.Job, bench);

        Assert.DoesNotContain("has no .avala/checks.json", section.Lacks, StringComparison.Ordinal);
    }

    private static Avala.Workbench.Timeline.ToolEntry Tool(string item, ItemKind kind, string input, ItemOutcome outcome) =>
        new($"item:{item}", kind, input, string.Empty, outcome) { Input = input };

    [Fact]
    public async Task TheEvidenceShowsWithoutWaitingForTheWorktreeToBeRead()
    {
        using var section = new EvidenceSectionViewModel(bench.Inspected(), links);
        var job = InspectedJobs.Reviewed(bench);
        bench.Workspaces.Reachable = new TaskCompletionSource().Task;

        await section.FocusAsync(job.Job, bench);

        Assert.Equal((true, "Verified on attempt 2 of 2"), (section.IsLoaded, section.Summary));
    }

    [Fact]
    public async Task LosingTheFocusEmptiesTheSection()
    {
        using var section = new EvidenceSectionViewModel(bench.Inspected(), links);
        await section.FocusAsync(InspectedJobs.Reviewed(bench).Job, bench);

        await section.FocusAsync(Option<JobId>.None, bench);

        Assert.Equal((false, string.Empty, 0), (section.IsLoaded, section.Summary, section.Attempts.Count));
    }

    [Fact]
    public async Task AJobWithoutVerificationsIsNotVerified()
    {
        using var section = new EvidenceSectionViewModel(bench.Inspected(), links);

        await section.FocusAsync(bench.Job("Fix JPY rounding in invoice totals", JobStatus.Running).Job, bench);

        Assert.Equal(("Not verified", 0), (section.Summary, section.Attempts.Count));
    }

    [Fact]
    public async Task ASectionReloadsWhenItsJobsRevisionMoves()
    {
        using var section = new EvidenceSectionViewModel(bench.Inspected(), links);
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
        using var section = new EvidenceSectionViewModel(bench.Inspected(), links);
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
        using var section = new EvidenceSectionViewModel(bench.Inspected(hooked), links);
        await bench.Ui.InvokeAsync(section.Activate, Cancellation);
        hooked.BeforeNext = section.Deactivate;

        await bench.Ui.InvokeAsync(() => section.OnRegionContextChanged(InspectedJobs.Reviewed(bench).Job), Cancellation);
        await (await bench.Ui.ReadAsync(() => section.Loading));

        Assert.Equal((true, false), (hooked.Hooked, await bench.Ui.ReadAsync(() => section.IsLoaded)));
    }

    [Fact]
    public async Task TheHeaderFactCountsTheChecksOfTheLastAttemptOrSaysNoneRanYet()
    {
        using var section = new EvidenceSectionViewModel(bench.Inspected(), links);
        await section.FocusAsync(bench.Job("Fix JPY rounding in invoice totals", JobStatus.Running).Job, bench);
        var running = section.Fact;

        await section.FocusAsync(InspectedJobs.Reviewed(bench).Job, bench);

        Assert.Equal(("no checks yet", "1 of 1 check passed"), (running, section.Fact));
    }

    [Fact]
    public async Task AnAttemptWhoseChecksARestartCutIsListedWithoutEvidenceBesideTheVerifiedOnes()
    {
        using var section = new EvidenceSectionViewModel(bench.Inspected(), links);
        var job = bench.Job("Fix JPY rounding in invoice totals", JobStatus.AwaitingReview).Job;
        bench.Catalog.Attempted(
            job,
            new AttemptRecord(1, AttemptOrigin.Initial, AttemptOutcome.Interrupted, Option<string>.None, Option<SessionId>.None),
            new AttemptRecord(2, AttemptOrigin.Recovery, AttemptOutcome.Rejected, Option<string>.None, Option<SessionId>.None),
            new AttemptRecord(3, AttemptOrigin.Retry, AttemptOutcome.Passed, Option<string>.None, Option<SessionId>.None));
        bench.Audit.Reports.AddRange([InspectedJobs.Report(job, 2, VerificationOutcome.Failed, CheckStatus.Failed, 1), InspectedJobs.Report(job, 3, VerificationOutcome.Passed, CheckStatus.Passed, 0)]);

        await section.FocusAsync(job, bench);

        Assert.Equal(("1 of 1 check passed", "Verified on attempt 3 of 3"), (section.Fact, section.Summary));
        Assert.Equal(
            ["Attempt 1: interrupted by a restart, no checks completed", "Attempt 2: failed · tests failed (exit 1, 1 s)", "Attempt 3: passed · tests passed (exit 0, 1 s)"],
            section.Attempts);
    }

    public void Dispose() => bench.Dispose();
}
