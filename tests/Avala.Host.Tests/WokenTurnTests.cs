using Avala.Jobs.Contracts;

namespace Avala.Host.Tests;

public sealed class WokenTurnTests(PublishedPlugins plugins)
{
    private const string Autonomous = """{ "autonomy": "autonomous" }""";

    private const string Survey = "Survey the greetings with a background agent and report what it finds.";

    private const string Notified = "The background agent finished: the project greets the team with a heading.";

    private const string Retried = "Read the background agent's tool results after Avala allowed it.";

    private const string Folded = "Folded your note: there are no farewells.";

    private const string Farewells = "Also list the farewells.";

    [Fact]
    public async Task AllowingADenialFromItsCardWhileABackgroundAgentStillWorksTellsTheAgentInItsTurnAndTheJobGoesOnAsync()
    {
        using var claude = await TranscribedClaude.PrepareAsync("woken-denial");
        await using var run = await SimulatedRun.TranscribedAsync(plugins, claude.Plugin, new JobRequest(string.Empty, Survey), claude.Data, [(".avala/permissions.json", Autonomous)]);
        var workbench = await run.WorkbenchAsync();
        var conversation = await workbench.SelectAsync(run.Job);
        await workbench.ShowsAsync(() =>
            conversation["Composer"]["Status"].Value<JobStatus>() == JobStatus.Running
            && OpenWorkbench.Entry(conversation, "DeniedCardViewModel") is { } card && card["CanAllow"].Value<bool>());

        await run.Ui.RunAsync(() => OpenWorkbench.Entry(conversation, "DeniedCardViewModel")!.Value.ExecuteAsync("AllowCommand"));
        await workbench.ShowsAsync(() =>
            conversation["Composer"]["Status"].Value<JobStatus>() == JobStatus.Failed
            || OpenWorkbench.Texts(conversation, "MessageViewModel", "Text").Contains(Retried));

        var (status, messages) = await run.Ui.ReadAsync(() => (conversation["Composer"]["Status"].Value<JobStatus>(), OpenWorkbench.Texts(conversation, "MessageViewModel", "Text")));
        Assert.NotEqual(JobStatus.Failed, status);
        Assert.Contains(Retried, messages);
    }

    [Fact]
    public async Task AllowingADenialWhileATurnTheAgentWokeOnItsOwnIsLiveTellsTheAgentInThatTurnWhichRunsItsJobAsync()
    {
        using var claude = await TranscribedClaude.PrepareAsync("woken-unannounced");
        await using var run = await SimulatedRun.TranscribedAsync(plugins, claude.Plugin, new JobRequest(string.Empty, Survey), claude.Data, [(".avala/permissions.json", Autonomous)]);
        var workbench = await run.WorkbenchAsync();
        var conversation = await workbench.SelectAsync(run.Job);
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        await workbench.ShowsAsync(() =>
            conversation["Composer"]["Status"].Value<JobStatus>() == JobStatus.Running
            && OpenWorkbench.Entry(conversation, "DeniedCardViewModel") is { } card && card["CanAllow"].Value<bool>());

        await run.Ui.RunAsync(() => OpenWorkbench.Entry(conversation, "DeniedCardViewModel")!.Value.ExecuteAsync("AllowCommand"));
        await workbench.ShowsAsync(() =>
            conversation["Composer"]["Status"].Value<JobStatus>() == JobStatus.Failed
            || OpenWorkbench.Texts(conversation, "MessageViewModel", "Text").Contains(Retried));

        var (status, messages) = await run.Ui.ReadAsync(() => (conversation["Composer"]["Status"].Value<JobStatus>(), OpenWorkbench.Texts(conversation, "MessageViewModel", "Text")));
        Assert.NotEqual(JobStatus.Failed, status);
        Assert.Contains(Retried, messages);
    }

    [Fact]
    public async Task AMessageSentWhileABackgroundAgentStillWorksJoinsTheAgentsTurnAndTheJobGoesOnAsync()
    {
        using var claude = await TranscribedClaude.PrepareAsync("woken-send-back");
        await using var run = await SimulatedRun.TranscribedAsync(plugins, claude.Plugin, new JobRequest(string.Empty, Survey), claude.Data, []);
        var workbench = await run.WorkbenchAsync();
        var conversation = await workbench.SelectAsync(run.Job);
        await workbench.ShowsAsync(() =>
            conversation["Composer"]["Status"].Value<JobStatus>() == JobStatus.Running
            && OpenWorkbench.Texts(conversation, "MessageViewModel", "Text").Contains(Notified));

        await run.Ui.RunAsync(() =>
        {
            conversation["Composer"].Set("Draft", Farewells);

            return conversation["Composer"].ExecuteAsync("SendCommand");
        });
        await workbench.ShowsAsync(() =>
            conversation["Composer"]["Status"].Value<JobStatus>() == JobStatus.Failed
            || OpenWorkbench.Texts(conversation, "MessageViewModel", "Text").Contains(Folded)
            || conversation["Composer"]["Queued"].Text == Farewells);

        var (status, messages, queued) = await run.Ui.ReadAsync(() => (
            conversation["Composer"]["Status"].Value<JobStatus>(),
            OpenWorkbench.Texts(conversation, "MessageViewModel", "Text"),
            conversation["Composer"]["Queued"].Text));
        Assert.NotEqual(JobStatus.Failed, status);
        Assert.True(messages.Contains(Folded) || queued == Farewells, $"The message neither joined the woken turn nor waits: [{string.Join(" | ", messages)}], queued '{queued}'");
    }
}
