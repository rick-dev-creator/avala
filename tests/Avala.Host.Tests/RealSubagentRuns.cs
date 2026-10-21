using System.Text.Json.Nodes;
using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Host.Tests;

public sealed class RealSubagentRuns(PublishedPlugins plugins)
{
    private const string Gate = "AVALA_REAL_CLAUDE";

    private const string Autonomous = """{ "autonomy": "autonomous" }""";

    private const string Instruction =
        "Use the Agent tool twice with run_in_background set to true to launch two general-purpose sub-agents. "
        + "Sub-agent A: run the Bash command `sleep 20 && echo A-done` and report its output. "
        + "Sub-agent B: run the Bash command `sleep 35 && echo B-done` and report its output. "
        + "After launching both, reply with the single word launched and end your turn without waiting for them.";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static string Login => Environment.GetEnvironmentVariable("AVALA_REAL_CLAUDE_LOGIN") is { Length: > 0 } folder
        ? folder
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude-work");

    private static string Output => Environment.GetEnvironmentVariable("AVALA_REAL_OUTPUT") is { Length: > 0 } folder
        ? folder
        : Path.Combine(Path.GetTempPath(), "avala-real-subagents");

    [Fact]
    public async Task BackgroundSubagentsInterruptedByAPersonAreRecordedWithWhatAvalaMadeOfThemAsync()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable(Gate) == "1", $"Set {Gate}=1, and AVALA_REAL_CLAUDE_LOGIN to a Claude Code login folder, to run real Claude Code.");
        var transcripts = Path.Combine(Output, "transcripts");
        Directory.CreateDirectory(transcripts);
        await using var run = await SimulatedRun.PreparedAsync(plugins, [("connections.json", Connections(transcripts))], [(".avala/permissions.json", Autonomous)], harnesses: true);
        var activity = run.Watch<AgentActivity>();
        var progressed = run.Watch<JobProgressed>();
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, Instruction)));
        var workbench = await run.WorkbenchAsync();
        var conversation = await workbench.SelectAsync(job);

        var before = await activity.CollectUntilAsync(seen => seen.Event is ItemStarted { Parent.IsSome: true });
        var states = new List<string> { $"before the interrupt: {await StateAsync(run, job)}" };
        await run.Ui.RunAsync(() => conversation["Composer"].ExecuteAsync("InterruptCommand"));
        var after = await activity.CollectUntilAsync(seen => seen.Event is TurnCompleted { Outcome: TurnOutcome.Finished });
        states.Add($"after the woken turn: {await StateAsync(run, job)}");
        var statuses = await progressed.CollectUntilAsync(found => found.Job == job && found.Status != JobStatus.Running);

        await File.WriteAllTextAsync(
            Path.Combine(Output, "avala-view.txt"),
            string.Join('\n', before.Concat(after).Select(seen => Describe(seen.Event)))
                + "\n\n" + string.Join('\n', statuses.Select(found => $"job {found.Status}"))
                + "\n\n" + string.Join('\n', states),
            Cancellation);
    }

    private static string Connections(string transcripts) => new JsonObject
    {
        ["connections"] = new JsonArray(new JsonObject
        {
            ["name"] = "claude",
            ["provider"] = "claude-code",
            ["credential"] = new JsonObject { ["source"] = "login", ["reference"] = Login },
            ["settings"] = new JsonObject { ["model"] = "haiku", ["transcripts"] = transcripts },
        }),
    }.ToJsonString();

    private static async Task<string> StateAsync(SimulatedRun run, JobId job)
    {
        await run.DeliveredAsync();
        var history = Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(job, Cancellation));

        return $"{history.Summary.Status} hold {history.Summary.Hold.Match(hold => hold.ToString(), () => "none")} attempts {string.Join(", ", history.Attempts.Select(attempt => $"{attempt.Origin}:{attempt.Outcome}"))}";
    }

    private static string Describe(IAgentEvent happened) => happened switch
    {
        TurnStarted started => $"turn started {started.Turn.Value} woken {started.Woken}",
        TurnCompleted completed => $"turn ended {completed.Turn.Value} {completed.Outcome}",
        ItemStarted started => $"item {started.Item.Value} {started.Kind} {started.Title} parent {started.Parent.Match(parent => parent.Value, () => "-")}",
        ItemBackgrounded backgrounded => $"backgrounded {backgrounded.Item.Value}",
        ItemCompleted completed => $"item ended {completed.Item.Value} {completed.Outcome}",
        _ => happened.GetType().Name,
    };
}
