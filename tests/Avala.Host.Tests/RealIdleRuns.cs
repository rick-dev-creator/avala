using System.Diagnostics;
using System.Text.Json.Nodes;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Agents.Contracts;
using Avala.Testing;

namespace Avala.Host.Tests;

[UsesRealGit]
public sealed class RealIdleRuns(PublishedPlugins plugins)
{
    private const string Gate = "AVALA_REAL_IDLE";

    private static readonly TimeSpan BetweenLooks = TimeSpan.FromMinutes(1);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static string Login => Environment.GetEnvironmentVariable("AVALA_REAL_CLAUDE_LOGIN") is { Length: > 0 } folder
        ? folder
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude-work");

    private static bool Wanted => Environment.GetEnvironmentVariable(Gate) == "1" && Environment.GetEnvironmentVariable("AVALA_REAL_CLAUDE") == "1";

    [Fact]
    public async Task ARealJobWaitingForReviewRestsOnceItsPromptCacheExpiresAndASendBackResumesItsConversationAsync()
    {
        Assert.SkipUnless(Wanted, $"Set {Gate}=1, AVALA_REAL_CLAUDE=1 and AVALA_REAL_CLAUDE_LOGIN to wait an hour and more with real Claude Code.");
        await using var run = await SimulatedRun.PreparedAsync(plugins, [("connections.json", Connections)], [], harnesses: true);
        using var pace = Paced(run);
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, "Reply with the single word ready. Do not change any file.")));
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(job));

        var session = Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(job, Cancellation)).Sessions[^1].Session;
        await UntilAsync(TimeSpan.FromMinutes(80), () => Task.FromResult(run.Get<IAgents>().IsOpen(session) ? Option<bool>.None : Option<bool>.Some(true)));
        var continued = Outcomes.Succeeds(await run.Get<IJobs>().SendBackAsync(job, "Which single word did you reply with? Answer with that word only.", Cancellation));

        Assert.Equal(ContinuedIn.ResumedConversation, continued.Conversation);
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(job));
    }

    [Fact]
    public async Task ARealJobWhoseBackgroundWorkFallsSilentForTwoHoursIsHeldAsStalledAndItsWorkStoppedAsync()
    {
        Assert.SkipUnless(Wanted, $"Set {Gate}=1, AVALA_REAL_CLAUDE=1 and AVALA_REAL_CLAUDE_LOGIN to wait two hours and more with real Claude Code.");
        await using var run = await SimulatedRun.PreparedAsync(plugins, [("connections.json", Connections)], [], harnesses: true);
        Directory.CreateDirectory(Path.Combine(run.Repository.Path, ".avala"));
        await File.WriteAllTextAsync(Path.Combine(run.Repository.Path, ".avala", "permissions.json"), """{ "autonomy": "autonomous" }""", Cancellation);
        await run.Repository.GitAsync(Cancellation, "add", "--all");
        await run.Repository.GitAsync(Cancellation, "commit", "--quiet", "-m", "Run autonomously");
        using var pace = Paced(run);
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, "Run the shell command `sleep 9137` with your Bash tool's run_in_background option, then end your turn at once without waiting for it and without checking on it.")));

        var held = await UntilAsync(TimeSpan.FromMinutes(150), async () =>
        {
            var summary = Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(job, Cancellation)).Summary;

            return summary.Status is JobStatus.Running or JobStatus.Preparing or JobStatus.Checking ? Option<JobSummary>.None : Option<JobSummary>.Some(summary);
        });

        Assert.Equal((JobStatus.NeedsHelp, Option<HoldReason>.Some(HoldReason.Stalled)), (held.Status, held.Hold));
        Assert.False(await SleepingAsync(), "The background command still runs after its job was held.");
    }

    private static ITimer Paced(SimulatedRun run)
    {
        var started = run.Clock.GetUtcNow();
        var real = Stopwatch.StartNew();

        return TimeProvider.System.CreateTimer(_ => run.AdvanceTo(started + real.Elapsed), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    private static async Task<T> UntilAsync<T>(TimeSpan patience, Func<Task<Option<T>>> look)
        where T : notnull
    {
        var deadline = DateTimeOffset.UtcNow + patience;

        while (true)
        {
            var seen = await look();

            if (seen.IsSome)
            {
                return seen.Match(found => found, () => throw new InvalidOperationException("Seen and gone."));
            }

            Assert.True(DateTimeOffset.UtcNow < deadline, $"Nothing happened within {patience}.");
            await WaitAsync(BetweenLooks);
        }
    }

    private static Task WaitAsync(TimeSpan wait)
    {
        var waited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timer = TimeProvider.System.CreateTimer(_ => waited.TrySetResult(), null, wait, Timeout.InfiniteTimeSpan);

        return waited.Task.ContinueWith(_ => timer.Dispose(), Cancellation, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    private static async Task<bool> SleepingAsync()
    {
        using var pgrep = Process.Start(new ProcessStartInfo("pgrep", ["-f", "sleep 9137"]) { RedirectStandardOutput = true }) ?? throw new InvalidOperationException("pgrep did not start.");
        await pgrep.WaitForExitAsync(Cancellation);

        return pgrep.ExitCode == 0;
    }

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
