using Avala.Agents.Contracts.Connections;
using Avala.Jobs.Contracts;

namespace Avala.Host.Tests;

public sealed class SteeringTests(PublishedPlugins plugins)
{
    private const string Steering = "Keep the old Purchases namespace as an alias.";

    private const string Late = "Also update the changelog.";

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
    public async Task AMessageSentWhileTheBoardStillShowsTheTurnRunningContinuesAJobThatAlreadyNeedsYouAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "hang");
        var workbench = await run.WorkbenchAsync();
        var conversation = await workbench.SelectAsync(run.Job);
        await run.ResumableAsync();
        await workbench.ShowsAsync(() => conversation["Composer"]["Placeholder"].Text == "Message the agent while it works…");

        var held = run.Board.HoldNextAsync();
        await run.Ui.RunAsync(() => conversation["Composer"].ExecuteAsync("InterruptCommand"));
        Assert.Equal(JobStatus.NeedsHelp, await run.SettledAsync());
        await held;
        await SendAsync(run, conversation, Steering);
        var behind = await run.Ui.ReadAsync(() => (conversation["Composer"]["Status"].Value<JobStatus>(), conversation["Composer"]["Queued"].Text));
        run.Board.Release();

        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        await workbench.ShowsAsync(() => OpenWorkbench.Texts(conversation, "PromptViewModel", "Origin").Count == 2);
        var (origins, texts, queued) = await run.Ui.ReadAsync(() => (
            OpenWorkbench.Texts(conversation, "PromptViewModel", "Origin"),
            OpenWorkbench.Texts(conversation, "PromptViewModel", "Text"),
            conversation["Composer"]["Queued"].Text));
        Assert.Equal((JobStatus.Running, string.Empty), behind);
        Assert.Equal(["Instruction", "You"], origins);
        Assert.Equal((Steering, string.Empty), (texts[1], queued));
    }

    [Fact]
    public async Task AMessageSentWhileTheBoardStillShowsTheTurnRunningWaitsInTheReviewOfAJobThatReachedItAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "steer");
        var workbench = await run.WorkbenchAsync();
        var conversation = await workbench.SelectAsync(run.Job);
        await workbench.ShowsAsync(() =>
            conversation["Composer"]["Placeholder"].Text == "Message the agent while it works…"
            && OpenWorkbench.Texts(conversation, "MessageViewModel", "Text").Contains("Renamed the folder. Tell me if anything else should change while I work."));

        var held = run.Board.HoldNextAsync();
        await SendAsync(run, conversation, Steering);
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        await held;
        await SendAsync(run, conversation, Late);
        var behind = await run.Ui.ReadAsync(() => conversation["Composer"]["Status"].Value<JobStatus>());
        var status = Assert.Single(await run.Get<IJobCatalog>().ListAsync(TestContext.Current.CancellationToken), summary => summary.Job == run.Job).Status;
        run.Board.Release();

        await workbench.ShowsAsync(() => conversation["Composer"]["Status"].Value<JobStatus>() == JobStatus.AwaitingReview);
        var (origins, queued, caption) = await run.Ui.ReadAsync(() => (
            OpenWorkbench.Texts(conversation, "PromptViewModel", "Origin"),
            conversation["Composer"]["Queued"].Text,
            conversation["Composer"]["QueuedCaption"].Text));
        Assert.Equal((JobStatus.Running, JobStatus.AwaitingReview), (behind, status));
        Assert.Equal(["Instruction"], origins);
        Assert.Equal((Late, "Queued · waits in the review: send back with it, or withdraw it"), (queued, caption));
    }

    [Fact]
    public async Task AMessageQueuedUntilTheJobReachesReviewWaitsThereAndIsSentBackOnlyWhenChosenAsync()
    {
        await using var run = await SimulatedRun.ConnectedAsync(
            plugins,
            "permission",
            new ConnectionName("plain"),
            [("connections.json", """{ "connections": [ { "name": "plain", "provider": "simulator", "settings": { "withoutCapabilities": "acceptsMessagesMidTurn" } } ] }""")]);
        var workbench = await run.WorkbenchAsync();
        var conversation = await workbench.SelectAsync(run.Job);
        Assert.Equal(Avala.Permissions.Contracts.DecisionDelivery.LeftToHuman, (await run.DecisionAsync()).Delivery);
        await workbench.ShowsAsync(() =>
            conversation["Composer"]["Placeholder"].Text == "This agent takes no message mid-turn · queue one for when it stops…"
            && OpenWorkbench.Entry(conversation, "PermissionCardViewModel") is { } card && card["AwaitsYou"].Value<bool>());

        await run.Ui.RunAsync(() =>
        {
            conversation["Composer"].Set("Draft", Steering);

            return conversation["Composer"].ExecuteAsync("SendCommand");
        });
        await run.Ui.RunAsync(() => OpenWorkbench.Entry(conversation, "PermissionCardViewModel")!.Value.ExecuteAsync("AllowCommand"));
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        await workbench.ShowsAsync(() => ((System.Windows.Input.ICommand)workbench.Page["OpenReviewCommand"].Target).CanExecute(null));
        var waiting = await run.Ui.ReadAsync(() => (conversation["Composer"]["Queued"].Text, OpenWorkbench.Texts(conversation, "PromptViewModel", "Origin").Count));

        await run.Ui.RunAsync(() => workbench.Page.ExecuteAsync("OpenReviewCommand"));
        var review = await run.Ui.ReadAsync(() => workbench.Page["Review"]);
        Assert.Equal(Steering, await run.Ui.ReadAsync(() => review["Queued"].Text));
        await run.Ui.RunAsync(() => review.ExecuteAsync("SendBackQueuedCommand"));

        Assert.Equal((Steering, 1), waiting);
        Assert.Equal("Sent back with the message you queued", await run.Ui.ReadAsync(() => review["Outcome"].Text));
        await workbench.ShowsAsync(() => OpenWorkbench.Texts(conversation, "PromptViewModel", "Origin").Count == 2);
        var (origins, texts, queued) = await run.Ui.ReadAsync(() => (
            OpenWorkbench.Texts(conversation, "PromptViewModel", "Origin"),
            OpenWorkbench.Texts(conversation, "PromptViewModel", "Text"),
            conversation["Composer"]["Queued"].Text));
        Assert.Equal(["Instruction", "Sent back"], origins);
        Assert.Equal((Steering, string.Empty), (texts[1], queued));
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

    private static Task SendAsync(SimulatedRun run, Bound conversation, string message) =>
        run.Ui.RunAsync(() =>
        {
            conversation["Composer"].Set("Draft", message);

            return conversation["Composer"].ExecuteAsync("SendCommand");
        });
}
