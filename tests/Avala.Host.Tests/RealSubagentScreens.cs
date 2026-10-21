using System.Text.Json.Nodes;
using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Testing.UI;

namespace Avala.Host.Tests;

public sealed class RealSubagentScreens(HeadlessUi ui, PublishedPlugins plugins)
{
    private const string Autonomous = """{ "autonomy": "autonomous" }""";

    private const string Nested =
        "Use the Agent tool once with run_in_background set to true to launch one general-purpose sub-agent with this task: "
        + "start the Bash command `sleep 20 && echo nested-done` with run_in_background set to true, then end your own work at once by replying started. "
        + "After launching the sub-agent, reply with the single word launched and end your turn. When you learn that the command finished, reply with its output.";

    private const string Two =
        "Use the Agent tool twice with run_in_background set to true to launch two general-purpose sub-agents. "
        + "Sub-agent A: run the Bash command `sleep 20 && echo A-done` and report its output. "
        + "Sub-agent B: run the Bash command `sleep 25 && echo B-done` and report its output. "
        + "After launching both, reply with the single word launched and end your turn without waiting for them.";

    private FeatureCamera Camera { get; } = new(ui, FeatureCamera.Folder ?? string.Empty);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static string Login => Environment.GetEnvironmentVariable("AVALA_REAL_CLAUDE_LOGIN") is { Length: > 0 } folder
        ? folder
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude-work");

    [Fact]
    public async Task ARealSubagentWithItsOwnBackgroundCommandIsShownWorkingThenCompletedAndTheJobReachesReviewAsync()
    {
        Gated();
        await using var run = await SimulatedRun.PreparedAsync(plugins, [("connections.json", Connections)], [(".avala/permissions.json", Autonomous)], harnesses: true);
        var activity = run.Watch<AgentActivity>();
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, Nested)));
        await OpenAsync(run, job);

        var nested = new HashSet<Agents.Contracts.Sessions.ItemId>();
        var subagents = new HashSet<Agents.Contracts.Sessions.ItemId>();
        _ = await activity.CollectUntilAsync(seen => Track(seen.Event, nested, subagents) is ItemBackgrounded backgrounded && nested.Contains(backgrounded.Item));
        await Camera.ShootAsync(run, "real-subagents-01-working-with-its-own-background-command");
        _ = await activity.CollectUntilAsync(seen => Track(seen.Event, nested, subagents) is ItemCompleted completed && subagents.Contains(completed.Item));
        await Camera.ShootAsync(run, "real-subagents-02-completed-while-its-command-still-runs");
        Assert.Equal(JobStatus.AwaitingReview, await SettledAsync(run, job));
        await Camera.ShootAsync(run, "real-subagents-03-job-in-review-after-the-command-ended");
        await Camera.ShootAsync(run, "real-subagents-03-job-in-review-after-the-command-ended-light", Avalonia.Styling.ThemeVariant.Light);
    }

    [Fact]
    public async Task TwoRealBackgroundSubagentsInterruptedByAPersonAreShownStoppedAndTheJobHeldAsync()
    {
        Gated();
        await using var run = await SimulatedRun.PreparedAsync(plugins, [("connections.json", Connections)], [(".avala/permissions.json", Autonomous)], harnesses: true);
        var activity = run.Watch<AgentActivity>();
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, Two)));
        var conversation = await OpenAsync(run, job);

        var backgrounded = 0;
        _ = await activity.CollectUntilAsync(seen => seen.Event is ItemBackgrounded && ++backgrounded == 2);
        _ = await activity.CollectUntilAsync(seen => seen.Event is ItemStarted { Parent.IsSome: true });
        await Camera.ShootAsync(run, "real-subagents-04-two-background-subagents-working");
        await run.Ui.RunAsync(() => conversation["Composer"].ExecuteAsync("InterruptCommand"));
        Assert.Equal(JobStatus.NeedsHelp, await SettledAsync(run, job));
        await run.DeliveredAsync();
        await Camera.ShootAsync(run, "real-subagents-05-interrupted-subagents-stopped-and-job-held");
    }

    private static IAgentEvent Track(IAgentEvent happened, HashSet<Agents.Contracts.Sessions.ItemId> nested, HashSet<Agents.Contracts.Sessions.ItemId> subagents)
    {
        if (happened is ItemStarted { Parent.IsSome: true } step)
        {
            nested.Add(step.Item);
        }
        else if (happened is ItemStarted { Kind: ItemKind.Subagent } subagent)
        {
            subagents.Add(subagent.Item);
        }

        return happened;
    }

    private static void Gated() =>
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable("AVALA_REAL_CLAUDE") == "1" && FeatureCamera.Folder is not null && HeadlessApp.Renders,
            $"Set AVALA_REAL_CLAUDE=1, AVALA_REAL_CLAUDE_LOGIN, {FeatureCamera.Gate} and AVALA_HEADLESS_RENDER=1 to capture real Claude Code sub-agents.");

    private static string Connections => new JsonObject
    {
        ["connections"] = new JsonArray(new JsonObject
        {
            ["name"] = "claude",
            ["provider"] = "claude-code",
            ["credential"] = new JsonObject { ["source"] = "login", ["reference"] = Login },
            ["settings"] = new JsonObject { ["model"] = "haiku" },
        }),
    }.ToJsonString();

    private static async Task<Bound> OpenAsync(SimulatedRun run, JobId job)
    {
        var workbench = await run.WorkbenchAsync();

        return await workbench.SelectAsync(job);
    }

    private static async Task<JobStatus> SettledAsync(SimulatedRun run, JobId job)
    {
        while (true)
        {
            var progressed = run.Watch<JobProgressed>();
            var status = Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(job, Cancellation)).Summary.Status;

            if (status is not JobStatus.Running and not JobStatus.Preparing and not JobStatus.Checking)
            {
                return status;
            }

            try
            {
                _ = await progressed.UntilAsync(found => found.Job == job && found.Status is not JobStatus.Running and not JobStatus.Preparing and not JobStatus.Checking);
            }
            catch (TimeoutException)
            {
                Cancellation.ThrowIfCancellationRequested();
            }
        }
    }
}
