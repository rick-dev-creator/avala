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
}
