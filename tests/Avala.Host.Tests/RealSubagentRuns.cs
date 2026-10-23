using System.Text.Json.Nodes;
using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Host.Tests;

[UsesRealGit]
public sealed class RealSubagentRuns(PublishedPlugins plugins)
{
    private const string Gate = "AVALA_REAL_CLAUDE";

    private const string Autonomous = """{ "autonomy": "autonomous" }""";

    private const string Interrupted =
        "Use the Agent tool twice with run_in_background set to true to launch two general-purpose sub-agents. "
        + "Sub-agent A: run the Bash command `sleep 20 && echo A-done` and report its output. "
        + "Sub-agent B: run the Bash command `sleep 35 && echo B-done` and report its output. "
        + "After launching both, reply with the single word launched and end your turn without waiting for them.";

    private const string Nested =
        "Use the Agent tool once with run_in_background set to true to launch one general-purpose sub-agent with this task: "
        + "start the Bash command `sleep 30 && echo nested-done` with run_in_background set to true, then end your own work at once by replying started, without waiting for the command. "
        + "After launching the sub-agent, reply with the single word launched and end your turn. When you learn that the command finished, reply with its output.";

    private const string RerunWithTools =
        "Use the Agent tool once with run_in_background set to true to launch one general-purpose sub-agent with this task: "
        + "start the Bash command `sleep 25 && echo nested-done > nested.txt` with run_in_background set to true and end your work at once by replying started. "
        + "When you are told that the command finished, use the Read tool to read nested.txt and reply with its content. "
        + "After launching the sub-agent, reply with the single word launched and end your turn. When the sub-agent reports the content, reply with it.";

    private const string Restarted =
        "Use the Agent tool once with run_in_background set to true to launch one general-purpose sub-agent with this task: run the Bash command `sleep 60 && echo survived` and report its output. "
        + "After launching it, reply with the single word launched and end your turn. If you are told the sub-agent stopped or was lost, say so plainly and name it.";

    private static readonly TimeSpan Guard = TimeSpan.FromMinutes(6);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static string Login => Environment.GetEnvironmentVariable("AVALA_REAL_CLAUDE_LOGIN") is { Length: > 0 } folder
        ? folder
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude-work");

    private static string Output => Environment.GetEnvironmentVariable("AVALA_REAL_OUTPUT") is { Length: > 0 } folder
        ? folder
        : Path.Combine(Path.GetTempPath(), "avala-real-subagents");

    [Fact]
    public Task ARequestTheHarnessRetriesIsRecordedWithWhatAvalaMadeOfItAsync() =>
        RecordAsync("retried", "Reply with the single word ready.", (_, _, _) => Task.CompletedTask);

    [Fact]
    public Task BackgroundSubagentsInterruptedByAPersonAreRecordedWithWhatAvalaMadeOfThemAsync() =>
        RecordAsync("interrupted", Interrupted, async (run, conversation, activity) =>
        {
            _ = await activity.CollectUntilAsync(seen => seen.Event is ItemStarted { Parent.IsSome: true });
            await run.Ui.RunAsync(() => conversation["Composer"].ExecuteAsync("InterruptCommand"));
        });

    [Fact]
    public Task ASubagentThatEndsWhileItsOwnBackgroundCommandRunsIsRecordedWithWhatAvalaMadeOfItAsync() =>
        RecordAsync("nested", Nested, (_, _, _) => Task.CompletedTask);

    [Fact]
    public Task ASubagentClaudeCodeStartsAgainThatUsesToolsInItsSecondRunIsRecordedWithWhatAvalaMadeOfItAsync() =>
        RecordAsync("rerun-tools", RerunWithTools, (_, _, _) => Task.CompletedTask);

    [Fact]
    public async Task ABackgroundSubagentStillWorkingWhenAvalaRestartsIsRecordedWithWhatTheResumedConversationSaysAsync()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable(Gate) == "1", $"Set {Gate}=1, and AVALA_REAL_CLAUDE_LOGIN to a Claude Code login folder, to run real Claude Code.");
        var folder = Path.Combine(Output, "restarted");
        var transcripts = Path.Combine(folder, "transcripts");
        Directory.CreateDirectory(transcripts);
        await using var run = await SimulatedRun.PreparedAsync(plugins, [("connections.json", Connections(transcripts))], [(".avala/permissions.json", Autonomous)], harnesses: true);
        var activity = run.Watch<AgentActivity>();
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, Restarted)));
        _ = await activity.CollectUntilAsync(seen => seen.Event is ItemStarted { Parent.IsSome: true });

        await run.RestartAsync();
        var resumed = run.Watch<AgentActivity>();
        var seen = new List<string>();
        using var guard = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        guard.CancelAfter(Guard);

        try
        {
            seen.AddRange((await SettledAsync(run, job, guard.Token)).Select(status => $"job {status}"));
        }
        catch (OperationCanceledException) when (guard.IsCancellationRequested && !Cancellation.IsCancellationRequested)
        {
            seen.Add($"the job never settled within {Guard}");
        }

        seen.Add(await StateAsync(run, job));
        await File.WriteAllTextAsync(Path.Combine(folder, "avala-view.txt"), string.Join('\n', seen), Cancellation);
        _ = resumed;
    }

    private async Task RecordAsync(string name, string instruction, Func<SimulatedRun, Bound, EventWatch<AgentActivity>, Task> act)
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable(Gate) == "1", $"Set {Gate}=1, and AVALA_REAL_CLAUDE_LOGIN to a Claude Code login folder, to run real Claude Code.");
        var folder = Path.Combine(Output, name);
        var transcripts = Path.Combine(folder, "transcripts");
        Directory.CreateDirectory(transcripts);
        await using var run = await SimulatedRun.PreparedAsync(plugins, [("connections.json", Connections(transcripts))], [(".avala/permissions.json", Autonomous)], harnesses: true);
        var activity = run.Watch<AgentActivity>();
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, instruction)));
        var conversation = await (await run.WorkbenchAsync()).SelectAsync(job);
        var seen = new List<string>();
        using var guard = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        guard.CancelAfter(Guard);

        try
        {
            await act(run, conversation, activity).WaitAsync(guard.Token);
            seen.AddRange((await SettledAsync(run, job, guard.Token)).Select(status => $"job {status}"));
        }
        catch (OperationCanceledException) when (guard.IsCancellationRequested && !Cancellation.IsCancellationRequested)
        {
            seen.Add($"the job never settled within {Guard}");
        }

        seen.Add(await StateAsync(run, job));
        await File.WriteAllTextAsync(Path.Combine(folder, "avala-view.txt"), string.Join('\n', seen), Cancellation);
    }

    private static async Task<IReadOnlyList<JobStatus>> SettledAsync(SimulatedRun run, JobId job, CancellationToken guard)
    {
        var seen = new List<JobStatus>();

        while (true)
        {
            var progressed = run.Watch<JobProgressed>();
            var status = Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(job, guard)).Summary.Status;
            seen.Add(status);

            if (status is not JobStatus.Running and not JobStatus.Preparing and not JobStatus.Checking)
            {
                return seen;
            }

            try
            {
                _ = await progressed.UntilAsync(found => found.Job == job && found.Status is not JobStatus.Running and not JobStatus.Preparing and not JobStatus.Checking).WaitAsync(guard);
            }
            catch (TimeoutException)
            {
                guard.ThrowIfCancellationRequested();
            }
        }
    }

    private static string Connections(string transcripts) => new JsonObject
    {
        ["default"] = "claude",
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

        return $"{history.Summary.Status} hold {history.Summary.Hold.Match(hold => hold.ToString(), () => "none")} attempts {string.Join(", ", history.Attempts.Select(attempt => $"{attempt.Origin}:{attempt.Outcome}"))} sessions open {run.Get<IAgents>().IsOpen(history.Sessions[^1].Session)} working {run.Get<IAgents>().IsWorking(history.Sessions[^1].Session)}";
    }
}
