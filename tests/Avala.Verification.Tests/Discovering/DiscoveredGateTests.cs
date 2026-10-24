using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Verification.Contracts;
using Avala.Verification.Discovering;

namespace Avala.Verification.Tests.Discovering;

public sealed class DiscoveredGateTests
{
    private const string Proposal = """{ "checks": [{ "name": "tests", "command": "npm", "arguments": ["test"] }] }""";

    [Fact]
    public async Task AnAttemptFinishedBeforeTheScoutWaitsForItThenRunsTheFoundChecksAsync()
    {
        await using var scouted = Scouted.Fresh();
        var job = await scouted.RunningAsync();
        scouted.Processes.Exits("npm", 0, TimeSpan.FromSeconds(8), "12 passing\n");

        var verdict = scouted.EvaluateAsync(job).AsTask();

        Assert.False(verdict.IsCompleted);
        Assert.Empty(scouted.Reports);
        scouted.Proposes(scouted.LastScout, Proposal);
        await scouted.MoveAsync(scouted.LastScout, JobStatus.AwaitingReview);
        Assert.Equal(GateVerdict.Pass, await verdict);
        var report = Assert.Single(scouted.Reports);
        Assert.Equal((VerificationOutcome.Passed, true, true, false), (report.Outcome, report.Discovered, report.DiscoveryShipped, report.Missing));
        Assert.Equal(["tests"], report.Checks.Select(check => check.Name));
        Assert.Equal(2, scouted.Processes.Requests.Count);
        Assert.Equal([(new Workspaces.Contracts.WorkspaceId(job.Value), DiscoveredChecks.ShippedLabel)], scouted.Jobs.Checkpoints);
        Assert.Equal(Option<string>.Some(Scouted.FakeJobs.Worktree(job)), scouted.Processes.Requests[^1].WorkingDirectory);
    }

    [Fact]
    public async Task AnAttemptFinishedBeforeAnyScoutStartedStartsOneAndWaitsForItAsync()
    {
        await using var scouted = Scouted.Fresh();
        var job = scouted.Job();

        var verdict = scouted.EvaluateAsync(job).AsTask();
        _ = await scouted.Bus.WaitForAsync<ChecksDiscoveryChanged>(changed => changed.Discovery.Job == job, Scouted.Cancellation);

        Assert.False(verdict.IsCompleted);
        await scouted.MoveAsync(scouted.LastScout, JobStatus.NeedsHelp);
        Assert.Equal(GateVerdict.Pass, await verdict);
        Assert.Equal((VerificationOutcome.NoChecksDeclared, true, false), Assert.Single(scouted.Reports) is var report ? (report.Outcome, report.Missing, report.Discovered) : default);
    }

    [Fact]
    public async Task AFailingFoundCheckSendsTheAgentBackWithoutBlamingItForTheDeclarationAsync()
    {
        await using var scouted = Scouted.Fresh();
        var job = await scouted.RunningAsync();
        scouted.Proposes(scouted.LastScout, Proposal);
        scouted.Processes.Exits("npm", 0, TimeSpan.FromSeconds(8));
        await scouted.MoveAsync(scouted.LastScout, JobStatus.AwaitingReview);
        scouted.Committed.Added(Scouted.FakeJobs.Worktree(job), Scouted.ChecksPath);
        scouted.Processes.Exits("npm", 1, TimeSpan.FromSeconds(8), "1 failing\n");

        var verdict = await scouted.EvaluateAsync(job, attempt: 2);

        Assert.Equal(
            GateVerdict.Retry("The repository check \"tests\" did not pass: `npm test` exited with code 1 after 8.0 s. Fix the cause, then finish the turn again.\n\nOutput:\n1 failing"),
            verdict);
    }

    [Fact]
    public async Task ADeclarationTheAgentChangedAfterAvalaShippedItIsNotShippedAndIsNotedAsync()
    {
        await using var scouted = Scouted.Fresh();
        var job = await scouted.RunningAsync();
        scouted.Proposes(scouted.LastScout, Proposal);
        scouted.Processes.Exits("npm", 0, TimeSpan.FromSeconds(8));
        await scouted.MoveAsync(scouted.LastScout, JobStatus.AwaitingReview);
        scouted.Folders.Draft(Scouted.FakeJobs.Worktree(job), Scouted.ChecksPath, """{ "checks": [] }""");
        scouted.Committed.Added(Scouted.FakeJobs.Worktree(job), Scouted.ChecksPath);
        scouted.Processes.Exits("npm", 1, TimeSpan.FromSeconds(8));

        var verdict = await scouted.EvaluateAsync(job, attempt: 2);

        var report = Assert.Single(scouted.Reports);
        Assert.Equal((true, false, Option<bool>.Some(true)), (report.Discovered, report.DiscoveryShipped, report.Declaration.Map(origin => origin.EditedInWorktree)));
        Assert.EndsWith("Your changes to .avala/checks.json do not apply to this job: its checks come from the commit it started from.", verdict.Feedback, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChecksShippedWhileTheAgentStillWorkedAreLeftToTheAttemptsOwnCheckpointAsync()
    {
        await using var scouted = Scouted.Fresh();
        var job = await scouted.RunningAsync();
        scouted.Proposes(scouted.LastScout, Proposal);
        scouted.Processes.Exits("npm", 0, TimeSpan.FromSeconds(8));
        await scouted.MoveAsync(scouted.LastScout, JobStatus.AwaitingReview);

        Assert.Equal(GateVerdict.Pass, await scouted.EvaluateAsync(job));

        Assert.True(Assert.Single(scouted.Reports).DiscoveryShipped);
        Assert.Empty(scouted.Jobs.Checkpoints);
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
}
