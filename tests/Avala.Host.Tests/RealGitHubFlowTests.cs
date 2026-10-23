using System.Diagnostics;
using System.Text.Json.Nodes;
using Avala.Forges.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Host.Tests;

[UsesRealGit]
public sealed class RealGitHubFlowTests(PublishedPlugins plugins)
{
    private const string Gate = "AVALA_REAL_GITHUB";

    private const string Instruction =
        "Add a function farewell(name) to greet.py that returns \"Goodbye, {name}!\", and a unittest for it in test_greet.py next to the existing one. Keep the existing code as it is.";

    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(20);

    private static readonly TimeSpan BetweenPolls = TimeSpan.FromSeconds(20);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static string Sandbox => Environment.GetEnvironmentVariable(Gate) ?? string.Empty;

    private static string Login => Environment.GetEnvironmentVariable("AVALA_REAL_CLAUDE_LOGIN") is { Length: > 0 } folder
        ? folder
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude-work");

    [Fact]
    public async Task ARealJobDeliveredThroughAPullRequestOnGitHubIsFollowedThroughItsCiAndEndsApprovedOnceAPersonMergesItAsync()
    {
        Assert.SkipUnless(Sandbox.Contains('/', StringComparison.Ordinal) && Environment.GetEnvironmentVariable("AVALA_REAL_CLAUDE") == "1", $"Set {Gate}=owner/repository of a sandbox, AVALA_REAL_CLAUDE=1 and AVALA_REAL_CLAUDE_LOGIN to run a real job through a real pull request.");
        await using var run = await SimulatedRun.PreparedAsync(plugins, [("connections.json", Connections), ("forges.json", Forges)], [], harnesses: true, realSignIns: true);
        await run.Repository.GitAsync(Cancellation, "remote", "add", "origin", $"https://github.com/{Sandbox}.git");
        await run.Repository.GitAsync(Cancellation, "fetch", "--quiet", "origin");
        await run.Repository.GitAsync(Cancellation, "reset", "--quiet", "--hard", "origin/main");
        await run.Repository.GitAsync(Cancellation, "branch", "--quiet", "--set-upstream-to=origin/main", "main");
        var opened = run.Watch<PullRequestOpened>();
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, Instruction)));
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(job));

        var refused = run.Watch<PullRequestNotOpened>();
        var approved = await run.Get<IJobs>().ApproveAsync(job, Cancellation);

        if (!approved.TryGetValue(out var approval, out var rejection))
        {
            Assert.Fail($"The approval was refused {rejection}: {(await refused.UntilAsync(found => found.Job == job)).Error}");
        }

        var pullRequest = (await opened.UntilAsync(found => found.Job == job)).PullRequest;
        var green = await PollUntilAsync(run, job, state => state.Status is WatchStatus.Green or WatchStatus.Ended or WatchStatus.NeedsPerson);
        await GhAsync("pr", "merge", pullRequest.Number.ToString(System.Globalization.CultureInfo.InvariantCulture), "--repo", Sandbox, "--squash", "--delete-branch");
        var merged = await PollUntilAsync(run, job, state => state.Ended.IsSome);

        Assert.Equal(PullRequestDelivery.Strategy, approval.Delivery.Strategy);
        Assert.Equal(WatchStatus.Green, green.Status);
        Assert.Equal(Option<WatchEnd>.Some(WatchEnd.Merged), merged.Ended);
        Assert.Equal(JobStatus.Approved, Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(job, Cancellation)).Summary.Status);
    }

    private static async Task<PullRequestWatchState> PollUntilAsync(SimulatedRun run, JobId job, Func<PullRequestWatchState, bool> done)
    {
        var deadline = DateTimeOffset.UtcNow + Patience;

        while (true)
        {
            var latest = Outcomes.Present(run.Get<IPullRequests>().Of(job));

            if (done(latest))
            {
                return latest;
            }

            Assert.True(DateTimeOffset.UtcNow < deadline, $"The pull request never got there within {Patience}; it was {latest.Status}");
            await WaitAsync(BetweenPolls);
            var changes = run.Watch<PullRequestWatchChanged>();
            var due = latest.NextPoll.Match(next => next, () => run.Clock.GetUtcNow());
            run.AdvanceTo(due);
            _ = await changes.UntilAsync(found => found.State.Job == job && found.State.LastPolled.Match(polled => polled >= due, () => false));
        }
    }

    private static Task WaitAsync(TimeSpan wait)
    {
        var waited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timer = TimeProvider.System.CreateTimer(_ => waited.TrySetResult(), null, wait, Timeout.InfiniteTimeSpan);

        return waited.Task.ContinueWith(_ => timer.Dispose(), Cancellation, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    private static async Task GhAsync(params string[] arguments)
    {
        var start = new ProcessStartInfo("gh") { RedirectStandardOutput = true, RedirectStandardError = true };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var gh = Process.Start(start) ?? throw new InvalidOperationException("gh did not start.");
        await gh.WaitForExitAsync(Cancellation);
        Assert.True(gh.ExitCode == 0, $"gh {string.Join(' ', arguments)} failed: {await gh.StandardError.ReadToEndAsync(Cancellation)}");
    }

    private static string Forges => """{ "pollSeconds": 30, "forges": [{ "name": "github-cli", "forge": "github", "credential": { "source": "cli" } }] }""";

    private static string Connections => new JsonObject
    {
        ["default"] = "claude",
        ["connections"] = new JsonArray(new JsonObject
        {
            ["name"] = "claude",
            ["provider"] = "claude-code",
            ["credential"] = new JsonObject { ["source"] = "login", ["reference"] = Login },
            ["settings"] = new JsonObject { ["model"] = "haiku" },
        }),
    }.ToJsonString();
}
