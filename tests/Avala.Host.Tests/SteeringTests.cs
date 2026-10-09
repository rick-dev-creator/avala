using Avala.Agents.Contracts.Connections;
using Avala.Jobs.Contracts;

namespace Avala.Host.Tests;

public sealed class SteeringTests(PublishedPlugins plugins)
{
    private const string Steering = "Keep the old Purchases namespace as an alias.";

    [Fact]
    public async Task AMessageWrittenWhileTheAgentWorksJoinsTheRunningTurnAndTheAgentAnswersItAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "steer");
        var workbench = await run.WorkbenchAsync();
        var conversation = await workbench.SelectAsync(run.Job);
        await workbench.ShowsAsync(() =>
            conversation["Composer"]["Placeholder"].Text == "Message the agent while it works…"
            && OpenWorkbench.Texts(conversation, "MessageViewModel", "Text").Contains("Renamed the folder. Tell me if anything else should change while I work."));

        await run.Ui.RunAsync(() =>
        {
            conversation["Composer"].Set("Draft", Steering);

            return conversation["Composer"].ExecuteAsync("SendCommand");
        });

        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        await workbench.ShowsAsync(() => OpenWorkbench.Entry(conversation, "TurnEndViewModel") is not null);
        var (kinds, interjection, answer) = await run.Ui.ReadAsync(() => (
            OpenWorkbench.Kinds(conversation).ToList(),
            OpenWorkbench.Texts(conversation, "InterjectionViewModel", "Text"),
            OpenWorkbench.Texts(conversation, "MessageViewModel", "Text")));
        Assert.Equal([Steering], interjection);
        Assert.Contains($"Noted: {Steering} I am folding it into this turn.", answer);
        Assert.Single(kinds, kind => kind == "TurnEndViewModel");
        Assert.True(kinds.IndexOf("InterjectionViewModel") < kinds.IndexOf("TurnEndViewModel"));
    }

    [Fact]
    public async Task OnAConnectionThatTakesNoMessageMidTurnTheMessageIsQueuedAndContinuesTheJobWhenItStopsAsync()
    {
        await using var run = await SimulatedRun.ConnectedAsync(
            plugins,
            "hang",
            new ConnectionName("plain"),
            [("connections.json", """{ "connections": [ { "name": "plain", "provider": "simulator", "settings": { "withoutCapabilities": "acceptsMessagesMidTurn" } } ] }""")]);
        var workbench = await run.WorkbenchAsync();
        var conversation = await workbench.SelectAsync(run.Job);
        await run.ResumableAsync();
        await workbench.ShowsAsync(() => conversation["Composer"]["Placeholder"].Text == "This agent takes no message mid-turn · queue one for when it stops…");

        await run.Ui.RunAsync(() =>
        {
            conversation["Composer"].Set("Draft", Steering);

            return conversation["Composer"].ExecuteAsync("SendCommand");
        });
        Assert.Equal(Steering, await run.Ui.ReadAsync(() => conversation["Composer"]["Queued"].Text));

        await run.Ui.RunAsync(() => conversation["Composer"].ExecuteAsync("InterruptCommand"));

        Assert.Equal(JobStatus.NeedsHelp, await run.SettledAsync());
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        await workbench.ShowsAsync(() => OpenWorkbench.Texts(conversation, "PromptViewModel", "Origin").Count == 2);
        var (origins, texts, queued) = await run.Ui.ReadAsync(() => (
            OpenWorkbench.Texts(conversation, "PromptViewModel", "Origin"),
            OpenWorkbench.Texts(conversation, "PromptViewModel", "Text"),
            conversation["Composer"]["Queued"].Text));
        Assert.Equal(["Instruction", "You"], origins);
        Assert.Equal((Steering, string.Empty), (texts[1], queued));
    }
}
