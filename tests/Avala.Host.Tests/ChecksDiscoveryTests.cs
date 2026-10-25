using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Autopilot.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Verification.Contracts;
using Avala.Workspaces.Contracts;

namespace Avala.Host.Tests;

public sealed class ChecksDiscoveryTests(PublishedPlugins plugins)
{
    private const string Found = """
        {
          "checks": [
            {
              "name": "tests",
              "command": "git",
              "arguments": [
                "--version"
              ],
              "timeoutSeconds": 60
            }
          ]
        }

        """;

    private static readonly (string Path, string Content) Autonomous = (".avala/permissions.json", """{ "autonomy": "autonomous" }""");

    private const string ScoutBrief = "Find out how this repository proves";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AJobInARepositoryWithoutChecksIsVerifiedByTheChecksAScoutFoundAndShipsThemUnflaggedAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, [SimulatedRun.DiscoveryOn], [Autonomous]);

        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("edit"))));

        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(job));
        var report = run.Get<IVerifications>().OfJob(job)[^1];
        Assert.Equal((VerificationOutcome.Passed, true, true), (report.Outcome, report.Discovered, report.DiscoveryShipped));
        Assert.Equal(["tests"], report.Checks.Select(check => check.Name));
        var discovery = Outcomes.Present(run.Get<IChecksDiscoveries>().OfJob(job));
        Assert.Equal((DiscoveryState.Found, "git --version"), (discovery.State, Assert.Single(discovery.Checks).Command));
        var scout = Assert.Single(await run.Get<IJobCatalog>().ChildrenAsync(job, Cancellation));
        Assert.Equal((true, JobStatus.Discarded), (scout.Companion, scout.Status));
        var worktree = Outcomes.Succeeds(await run.Get<IWorkspaces>().FindAsync(Outcomes.Present((await run.Get<IJobCatalog>().HistoryAsync(job, Cancellation)).Bind(history => history.Summary.Workspace)), Cancellation)).Path;
        Assert.Equal(Found.ReplaceLineEndings("\n"), await File.ReadAllTextAsync(Path.Combine(worktree, ".avala", "checks.json"), Cancellation));
        var evidence = Outcomes.Present(await run.Get<IRunEvidence>().OfJobAsync(job, Cancellation));
        Assert.Empty(evidence.RuleFiles);
        Assert.Contains(".avala/checks.json", evidence.Summary.FilesChanged);
        Assert.Empty(await run.BrokenInvariantsAsync());
    }

    [Fact]
    public async Task AJobInARepositoryThatDeclaresChecksStartsNoScoutAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(
            plugins,
            [SimulatedRun.DiscoveryOn],
            [Autonomous, (".avala/checks.json", """{ "checks": [{ "name": "git", "command": "git", "arguments": ["--version"] }] }""")]);

        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("edit"))));

        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(job));
        Assert.Empty(await run.Get<IJobCatalog>().ChildrenAsync(job, Cancellation));
        Assert.False(run.Get<IVerifications>().OfJob(job)[^1].Discovered);
    }

    [Fact]
    public async Task AJobWhoseCompletionGateStartsTheScoutIsVerifiedByItAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, [SimulatedRun.DiscoveryOff], [Autonomous]);
        var progress = run.Watch<JobProgressed>();
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("discover-checks-on-cue"))));
        _ = await progress.UntilAsync(update => update.Job == job && update.Status == JobStatus.Running);
        await run.DeliveredAsync();
        await File.WriteAllTextAsync(Path.Combine(run.DataFolder, SimulatedRun.DiscoveryOn.File), SimulatedRun.DiscoveryOn.Content, Cancellation);

        _ = Outcomes.Succeeds(await run.Get<IJobs>().SteerAsync(job, new JobMessage("Go on."), Cancellation));

        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(job));
        Assert.True(run.Get<IVerifications>().OfJob(job)[^1].Discovered);
    }

    [Fact]
    public async Task AvalaFinishesStartingAfterARestartWhileAJobWaitsForItsScoutAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, [SimulatedRun.DiscoveryOn], [Autonomous]);
        run.Cues.Cue(ScoutBrief, "discover-checks-on-cue");
        var progress = run.Watch<JobProgressed>();
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("edit"))));
        _ = await progress.UntilAsync(update => update.Job == job && update.Status == JobStatus.Checking);
        await run.DeliveredAsync();

        await run.RestartAsync();

        await run.StartedAsync();
        Assert.Single(await run.Get<IJobCatalog>().ChildrenAsync(job, Cancellation));
        Assert.Equal(JobStatus.Checking, Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(job, Cancellation)).Summary.Status);
    }

    [Fact]
    public async Task InASupervisedRepositoryTheJobWaitsForThePersonToUseTheProposedChecksAndIsVerifiedByThemAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, [SimulatedRun.DiscoveryOn], []);
        var discoveries = run.Watch<ChecksDiscoveryChanged>();
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("reply"))));
        _ = await discoveries.UntilAsync(changed => changed.Discovery.Job == job && changed.Discovery.State == DiscoveryState.Proposed);
        await run.DeliveredAsync();
        var waiting = Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(job, Cancellation)).Summary.Status;

        await run.Get<IChecksProposals>().AcceptAsync(job, Cancellation);

        Assert.Equal(JobStatus.Checking, waiting);
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(job));
        var report = run.Get<IVerifications>().OfJob(job)[^1];
        Assert.Equal((VerificationOutcome.Passed, true), (report.Outcome, report.Discovered));
        Assert.Equal(["tests"], report.Checks.Select(check => check.Name));
    }

    [Fact]
    public async Task TheScoutOfASupervisedRepositoryNeverWaitsForAPersonItIsHiddenFromAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, [SimulatedRun.DiscoveryOn], []);
        run.Cues.Cue(ScoutBrief, "permission");
        var answered = run.Watch<Permissions.Contracts.PermissionAnswered>();
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("reply"))));

        var denied = (await answered.UntilAsync(_ => true)).Answer;

        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(job));
        Assert.NotEqual(Option<JobId>.Some(job), denied.Job);
        Assert.Equal(PermissionAnswer.Deny, denied.Answer);
        Assert.Equal(VerificationOutcome.NoChecksDeclared, run.Get<IVerifications>().OfJob(job)[^1].Outcome);
    }
}
