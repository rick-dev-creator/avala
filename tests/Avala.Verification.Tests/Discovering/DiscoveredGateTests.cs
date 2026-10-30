using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Verification.Contracts;
using Avala.Verification.Discovering;
using Avala.Workspaces.Contracts;

namespace Avala.Verification.Tests.Discovering;

public sealed class DiscoveredGateTests
{
    private const string Proposal = """{ "checks": [{ "name": "tests", "command": "npm", "arguments": ["test"] }] }""";

    [Fact]
    public async Task AnAttemptFinishedBeforeTheScoutIsDeferredWithoutEvidenceAndJudgedOnceTheChecksAreFoundAsync()
    {
        await using var scouted = Scouted.Fresh();
        var job = await scouted.RunningAsync();

        var deferred = await scouted.EvaluateAsync(job);

        Assert.Equal((GateVerdict.Deferred, 0), (deferred, scouted.Reports.Count));
        await FoundAsync(scouted);
        Assert.Equal([job], scouted.Rechecked);
        scouted.Processes.Exits("npm", 0, TimeSpan.FromSeconds(8), "12 passing\n");
        Assert.Equal(GateVerdict.Pass, await scouted.EvaluateAsync(job));
        var report = Assert.Single(scouted.Reports);
        Assert.Equal((VerificationOutcome.Passed, true, true, false), (report.Outcome, report.Discovered, report.DiscoveryShipped, report.Missing));
        Assert.Equal(Option<string>.Some(Scouted.FakeJobs.Worktree(job)), scouted.Processes.Requests[^1].WorkingDirectory);
    }

    [Fact]
    public async Task AnAttemptFinishedBeforeAnyScoutStartedStartsOneAndIsDeferredAsync()
    {
        await using var scouted = Scouted.Fresh();
        var job = scouted.Job();

        var verdict = await scouted.EvaluateAsync(job);

        Assert.Equal(GateVerdict.Deferred, verdict);
        Assert.Equal(Option<JobId>.Some(job), Assert.Single(scouted.Jobs.Submitted).Request.Parent);
    }

    [Fact]
    public async Task ChecksShippedAfterTheAttemptWasCommittedAreCommittedWithACheckpointOfTheirOwnOnceAsync()
    {
        await using var scouted = Scouted.Fresh();
        var job = await scouted.RunningAsync();
        await FoundAsync(scouted);
        scouted.Processes.Exits("npm", 0, TimeSpan.FromSeconds(8));

        _ = await scouted.EvaluateAsync(job);
        scouted.Jobs.Commit(job, Scouted.ChecksPath);
        _ = await scouted.EvaluateAsync(job, attempt: 2);

        Assert.Equal([(new WorkspaceId(job.Value), ShippedChecks.Label)], scouted.Jobs.Checkpoints);
    }

    [Fact]
    public async Task AFailingFoundCheckSendsTheAgentBackWithoutBlamingItForTheDeclarationAsync()
    {
        await using var scouted = Scouted.Fresh();
        var job = await scouted.RunningAsync();
        await FoundAsync(scouted);
        scouted.Committed.Added(Scouted.FakeJobs.Worktree(job), Scouted.ChecksPath);
        scouted.Processes.Exits("npm", 1, TimeSpan.FromSeconds(8), "1 failing\n");

        var verdict = await scouted.EvaluateAsync(job);

        Assert.Equal(
            GateVerdict.Retry("The repository check \"tests\" did not pass: `npm test` exited with code 1 after 8.0 s. Fix the cause, then finish the turn again.\n\nOutput:\n1 failing"),
            verdict);
    }

    [Fact]
    public async Task ADeclarationTheAgentChangedAfterAvalaShippedItIsNotShippedAndIsNotedAsync()
    {
        await using var scouted = Scouted.Fresh();
        var job = await scouted.RunningAsync();
        await FoundAsync(scouted);
        scouted.Folders.Draft(Scouted.FakeJobs.Worktree(job), Scouted.ChecksPath, """{ "checks": [] }""");
        scouted.Committed.Added(Scouted.FakeJobs.Worktree(job), Scouted.ChecksPath);
        scouted.Processes.Exits("npm", 1, TimeSpan.FromSeconds(8));

        var verdict = await scouted.EvaluateAsync(job);

        var report = Assert.Single(scouted.Reports);
        Assert.Equal((true, false, Option<bool>.Some(true)), (report.Discovered, report.DiscoveryShipped, report.Declaration.Map(origin => origin.EditedInWorktree)));
        Assert.EndsWith("Your changes to .avala/checks.json do not apply to this job: its checks are the ones Avala found for this repository.", verdict.Feedback, StringComparison.Ordinal);
        Assert.Empty(scouted.Jobs.Checkpoints);
    }

    [Fact]
    public async Task AChildOfAJobWhoseChecksWereFoundIsVerifiedByThemAsync()
    {
        await using var scouted = Scouted.Fresh();
        var parent = await scouted.RunningAsync();
        var child = scouted.Job(parent: parent);

        var deferred = await scouted.EvaluateAsync(child);
        await FoundAsync(scouted);
        scouted.Processes.Exits("npm", 0, TimeSpan.FromSeconds(8));
        var verdict = await scouted.EvaluateAsync(child);

        Assert.Equal((GateVerdict.Deferred, GateVerdict.Pass), (deferred, verdict));
        Assert.Contains(child, scouted.Rechecked);
        Assert.Equal((VerificationOutcome.Passed, true), Assert.Single(scouted.Reports) is var report ? (report.Outcome, report.Discovered) : default);
        Assert.Empty(scouted.Jobs.Checkpoints);
    }

    [Fact]
    public async Task TheScoutItselfIsNeverHeldByTheDiscoveryItServesAsync()
    {
        await using var scouted = Scouted.Fresh();
        _ = await scouted.RunningAsync();
        _ = scouted.Committed.Workspace(Scouted.FakeJobs.Worktree(scouted.LastScout));

        var verdict = await scouted.EvaluateAsync(scouted.LastScout);

        Assert.Equal(GateVerdict.Pass, verdict);
    }

    [Fact]
    public async Task AJobWithoutADiscoveryPassesAsDeclaringNoChecksAsync()
    {
        await using var scouted = Scouted.Fresh();
        scouted.Settings.Discovers = false;
        var job = await scouted.RunningAsync();

        Assert.Equal(GateVerdict.Pass, await scouted.EvaluateAsync(job));

        Assert.Equal((VerificationOutcome.NoChecksDeclared, true, false), Assert.Single(scouted.Reports) is var report ? (report.Outcome, report.Missing, report.Discovered) : default);
    }

    private static async Task FoundAsync(Scouted scouted)
    {
        await scouted.ProposesAsync(scouted.LastScout, Proposal);
        scouted.Processes.Exits("npm", 0, TimeSpan.FromSeconds(8));
        await scouted.MoveAsync(scouted.LastScout, JobStatus.AwaitingReview);
    }
}
