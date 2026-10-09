using Avala.Jobs.Contracts;

namespace Avala.Host.Tests;

public sealed class ClaudeCodeSteeringTests(PublishedPlugins plugins)
{
    [Fact]
    public async Task AClaudeCodeMessageMidTurnJoinsTheTurnAndOneQueuedBeforeAnInterruptionNeverLeaksIntoTheNextTurnAsync()
    {
        using var claude = await TranscribedClaude.PrepareAsync("steer-interrupt");
        await using var run = await SimulatedRun.TranscribedAsync(
            plugins,
            claude.Plugin,
            new JobRequest(string.Empty, "Count from 1 to 40, one number per line, nothing else."),
            claude.Data,
            []);
        var workbench = await run.WorkbenchAsync();
        var conversation = await workbench.SelectAsync(run.Job);
        await workbench.ShowsAsync(() =>
            conversation["Composer"]["Placeholder"].Text == "Message the agent while it works…"
            && OpenWorkbench.Texts(conversation, "MessageViewModel", "Text").Any(text => text.StartsWith("1\n2\n3\n4", StringComparison.Ordinal)));

        await SendAsync(run, workbench, conversation, JobStatus.Running, "Then end with the single word banana.");
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        await workbench.ShowsAsync(() => OpenWorkbench.Kinds(conversation).Contains("TurnEndViewModel"));
        var steered = await run.Ui.ReadAsync(() => (OpenWorkbench.Kinds(conversation).Count(kind => kind == "TurnEndViewModel"), string.Join('|', OpenWorkbench.Texts(conversation, "MessageViewModel", "Text"))));

        await SendAsync(run, workbench, conversation, JobStatus.AwaitingReview, "Count from 1 to 60, one number per line, nothing else.");
        await workbench.ShowsAsync(() => OpenWorkbench.Texts(conversation, "MessageViewModel", "Text").Contains("1"));
        await SendAsync(run, workbench, conversation, JobStatus.Running, "Then end with the single word cherry.");
        await run.Ui.RunAsync(() => conversation["Composer"].ExecuteAsync("InterruptCommand"));
        Assert.Equal(JobStatus.NeedsHelp, await run.SettledAsync());

        await SendAsync(run, workbench, conversation, JobStatus.NeedsHelp, "Reply with the single word ok.");
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        await workbench.ShowsAsync(() => OpenWorkbench.Kinds(conversation).Count(kind => kind == "TurnEndViewModel") == 3);
        var (messages, interjections, turnEnds) = await run.Ui.ReadAsync(() => (
            OpenWorkbench.Texts(conversation, "MessageViewModel", "Text"),
            OpenWorkbench.Texts(conversation, "InterjectionViewModel", "Text"),
            OpenWorkbench.Texts(conversation, "TurnEndViewModel", "Summary")));

        Assert.Equal(1, steered.Item1);
        Assert.Contains("banana", steered.Item2, StringComparison.Ordinal);
        Assert.Equal(["Then end with the single word banana.", "Then end with the single word cherry."], interjections);
        Assert.Equal("ok", messages[^1]);
        Assert.DoesNotContain(messages, text => text.Contains("cherry", StringComparison.Ordinal));
        Assert.Equal(3, turnEnds.Count);
    }

    private static async Task SendAsync(SimulatedRun run, OpenWorkbench workbench, Bound conversation, JobStatus shown, string message)
    {
        await workbench.ShowsAsync(() => conversation["Composer"]["Status"].Value<JobStatus>() == shown);
        await run.Ui.RunAsync(() =>
        {
            conversation["Composer"].Set("Draft", message);

            return conversation["Composer"].ExecuteAsync("SendCommand");
        });
    }
}
