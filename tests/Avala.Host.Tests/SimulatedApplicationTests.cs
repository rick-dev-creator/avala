using System.Runtime.Loader;
using System.Xml.Linq;
using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Canvas.Contracts;
using Avala.Host.Composition;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Verification.Contracts;
using Avala.Workspaces.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Host.Tests;

public sealed class SimulatedApplicationTests(PublishedPlugins plugins)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ThePublishedPluginsComposeEveryModuleWithOneIdentityPerAssemblyAsync()
    {
        using var data = new TemporaryFolder();
        await using var root = CompositionRoot.Create(plugins.Directory, new AvalaPaths(data.Path));

        Assert.NotNull(root.Services.GetRequiredService<IAgents>());
        Assert.NotNull(root.Services.GetRequiredService<IWorkspaces>());
        Assert.NotNull(root.Services.GetRequiredService<IJobs>());
        Assert.NotNull(root.Services.GetRequiredService<IUsage>());
        Assert.NotNull(root.Services.GetRequiredService<IVerifications>());
        Assert.Equal("simulator", Assert.Single(root.Services.GetServices<IAgentProvider>()).Info.Id);
        Assert.Empty(AssemblyLoadContext.All
            .SelectMany(context => context.Assemblies)
            .Select(assembly => assembly.GetName().Name)
            .Where(name => name?.StartsWith("Avala.", StringComparison.Ordinal) == true)
            .GroupBy(name => name)
            .Where(copies => copies.Count() > 1)
            .Select(copies => copies.Key));
    }

    [Fact]
    public async Task AReplyJobAwaitsReviewAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "reply");

        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
    }

    [Fact]
    public async Task AnEditLandsInTheWorktreeAndInItsCheckpointAsync()
    {
        const string Greeting = "# Hello\n\nWritten by the simulator.\n";
        await using var run = await SimulatedRun.StartAsync(plugins, "edit");

        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        Assert.Equal(Greeting, await File.ReadAllTextAsync(Path.Combine(run.Worktree, "GREETING.md"), Cancellation));
        Assert.Equal("Attempt 1", await run.Repository.GitInAsync(run.Worktree, Cancellation, "log", "-1", "--format=%s"));
        Assert.Equal(Greeting.Trim(), await run.Repository.GitInAsync(run.Worktree, Cancellation, "show", "HEAD:GREETING.md"));
    }

    [Fact]
    public async Task ACrashingAgentFailsTheJobAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "crash");

        Assert.Equal(JobStatus.Failed, await run.SettledAsync());
    }

    [Fact]
    public async Task AnItemLeftOpenIsAbandonedBeforeTheTurnFinishesAndTheJobAwaitsReviewAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "left-open");

        var turn = await run.TurnAsync();

        Assert.Equal(TurnOutcome.Finished, Assert.IsType<TurnCompleted>(turn[^1]).Outcome);
        Assert.Contains(turn.SkipLast(1), update => update is ItemCompleted { Outcome: ItemOutcome.Abandoned, Item.Value: "build" });
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
    }

    [Fact]
    public async Task StreamedCanvasesArriveInOrderAsSnapshotsThatEndCompleteAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "canvas");

        var snapshots = await run.CanvasSnapshotsAsync(canvasCount: 2);

        Assert.Equal("svg", XDocument.Parse(Canvas(snapshots, "diagram", "image/svg+xml")).Root?.Name.LocalName);
        Assert.Equal(
            "flowchart LR\n  Submitted --> Running\n  Running --> Checking --> AwaitingReview\n",
            Canvas(snapshots, "flow", "text/vnd.mermaid"));
        Assert.Equal(
            [("diagram", CanvasStatus.Completed), ("flow", CanvasStatus.Completed)],
            run.Canvases.InSession(snapshots[0].Session).Select(canvas => (canvas.Canvas.Item.Value, canvas.Status)));
    }

    [Fact]
    public async Task UsageWithCostAndAUsageLimitReachTheBusAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "reply");

        var turn = await run.TurnAsync();

        var usage = Assert.Single(turn.OfType<UsageReported>());
        Assert.True(usage.Tokens.Input > 0 && usage.Tokens.Output > 0);
        Assert.True(usage.Cost.Match(cost => cost is { Amount: > 0, Currency: "USD" }, () => false));
        Assert.InRange(Assert.Single(turn.OfType<LimitReported>()).Limit.UsedFraction, double.Epsilon, 1);
    }

    [Fact]
    public async Task ASimulatedJobShowsItsUsageCostAndLimitInTheAggregatesAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "reply");

        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());

        var usage = run.Get<IUsage>();
        var job = Outcomes.Present(usage.OfJob(run.Job));
        Assert.Equal(new TokenUsage(1_200, 80, 600, 120, 20), job.Tokens);
        Assert.Equal([new Cost(0.0042m, "USD")], job.Costs);
        Assert.Equal(1, job.Turns.Finished);
        Assert.Equal([new UsageLimit("5h", 0.12, Option<DateTimeOffset>.None)], job.Limits);
        var provider = Assert.Single(usage.ByProvider());
        Assert.Equal("simulator", provider.Provider.Id);
        Assert.Equal(job.Tokens, provider.Usage.Tokens);
    }

    [Fact]
    public async Task AJobReachesReviewOnlyAfterItsDeclaredCheckPassesWithTheEvidenceOfEveryAttemptAsync()
    {
        const string Checks = """
            {
              "checks": [
                { "name": "git", "command": "git", "arguments": ["--version"] },
                { "name": "calculator", "command": "git", "arguments": ["grep", "--quiet", "--fixed-strings", "add(2, 2) = 4", "--", "calculator.txt"], "timeoutSeconds": 60 }
              ]
            }
            """;
        await using var run = await SimulatedRun.StartAsync(plugins, "fix-after-feedback", (".avala/checks.json", Checks));

        var journey = await run.JourneyAsync();

        Assert.Equal(
            [JobStatus.Running, JobStatus.Checking, JobStatus.Running, JobStatus.Checking, JobStatus.AwaitingReview],
            journey.SkipWhile(status => status != JobStatus.Running));
        var reports = run.Get<IVerifications>().OfJob(run.Job);
        Assert.Equal(
            ["1 Failed Retry: git Passed 0, calculator Failed 1", "2 Passed Pass: git Passed 0, calculator Passed 0"],
            reports.Select(report => $"{report.Attempt} {report.Outcome} {report.Verdict.Decision}: "
                + string.Join(", ", report.Checks.Select(check => $"{check.Name} {check.Status} {check.ExitCode.Match(code => code, () => -1)}"))));
        Assert.StartsWith("git version", reports[0].Checks[0].OutputTail, StringComparison.Ordinal);
        Assert.Contains(
            "\"calculator\" did not pass: `git grep --quiet --fixed-strings \"add(2, 2) = 4\" -- calculator.txt` exited with code 1",
            reports[0].Verdict.Feedback,
            StringComparison.Ordinal);
        Assert.Equal("add(2, 2) = 4\n", await File.ReadAllTextAsync(Path.Combine(run.Worktree, "calculator.txt"), Cancellation));
    }

    private static string Canvas(IReadOnlyList<CanvasSnapshot> snapshots, string item, string mediaType)
    {
        var canvas = snapshots.Where(snapshot => snapshot.Canvas.Item == new ItemId(item)).ToList();

        Assert.All(canvas, snapshot => Assert.Equal(mediaType, snapshot.MediaType));
        Assert.All(canvas.SkipLast(1), snapshot => Assert.Equal(CanvasStatus.Streaming, snapshot.Status));
        Assert.Equal(CanvasStatus.Completed, canvas[^1].Status);
        Assert.All(canvas.Zip(canvas.Skip(1)), pair => Assert.StartsWith(pair.First.Content, pair.Second.Content, StringComparison.Ordinal));

        return canvas[^1].Content;
    }
}
