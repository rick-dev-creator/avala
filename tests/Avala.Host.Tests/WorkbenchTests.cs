using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;

namespace Avala.Host.Tests;

public sealed class WorkbenchTests(PublishedPlugins plugins)
{
    private const string Autonomous = """{ "autonomy": "autonomous" }""";

    [Fact]
    public async Task AJobStreamsIntoItsConversationInOrderWithItsThinkingAndToolRowsAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "edit", (".avala/permissions.json", Autonomous));
        var workbench = await run.WorkbenchAsync();
        var conversation = await workbench.SelectAsync(run.Job);
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());

        await workbench.ShowsAsync(() => OpenWorkbench.Entry(conversation, "TurnEndViewModel") is not null);

        var entries = await run.Ui.ReadAsync(() => conversation["Entries"].Items);
        Assert.Equal(
            ["PromptViewModel", "ReasoningViewModel", "PlanViewModel", "ToolViewModel", "ToolViewModel", "MessageViewModel", "TurnEndViewModel"],
            entries.Select(entry => entry.Kind));
        var (prompt, reasoning, plan, message) = await run.Ui.ReadAsync(() => (
            entries[0]["Text"].Text,
            (entries[1]["Text"].Text, entries[1]["IsThinking"].Value<bool>(), entries[1]["Summary"].Text.StartsWith("Thought for", StringComparison.Ordinal)),
            entries[2]["Progress"].Text,
            entries[5]["Text"].Text));
        Assert.Equal(SimulatedRun.Simulate("edit"), prompt);
        Assert.Equal(("I will add a greeting and run the tests.", false, true), reasoning);
        Assert.Equal("2 of 2", plan);
        Assert.Equal(
            [("FileEdit", "Edit GREETING.md", "done"), ("Command", "dotnet test", "done")],
            await run.Ui.ReadAsync(() => entries.Where(entry => entry.Kind == "ToolViewModel").Select(tool => (tool["Kind"].Text, tool["Title"].Text, tool["Outcome"].Text)).ToList()));
        Assert.Equal("Added GREETING.md and the tests pass.", message);
    }

    [Fact]
    public async Task APermissionCardAnsweredFromTheConversationUnblocksTheJobAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "permission");
        var workbench = await run.WorkbenchAsync();
        var conversation = await workbench.SelectAsync(run.Job);
        Assert.Equal(DecisionDelivery.LeftToHuman, (await run.DecisionAsync()).Delivery);
        await workbench.ShowsAsync(() => OpenWorkbench.Entry(conversation, "PermissionCardViewModel") is { } card && card["AwaitsYou"].Value<bool>());

        await run.Ui.RunAsync(() => OpenWorkbench.Entry(conversation, "PermissionCardViewModel")!.Value.ExecuteAsync("AllowCommand"));

        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        await workbench.ShowsAsync(() => OpenWorkbench.Texts(conversation, "MessageViewModel", "Text").Contains("The database is up to date."));
        Assert.Equal(
            ("Allowed", false, "dotnet ef database update"),
            await run.Ui.ReadAsync(() => OpenWorkbench.Entry(conversation, "PermissionCardViewModel")!.Value is var card
                ? (card["Verdict"].Text, card["AwaitsYou"].Value<bool>(), card["Target"].Text)
                : default));
    }

    [Fact]
    public async Task AFormIsAnsweredWithItsRecommendedOptionFromTheConversationAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "question");
        var workbench = await run.WorkbenchAsync();
        var conversation = await workbench.SelectAsync(run.Job);
        Assert.Equal(DecisionDelivery.LeftToHuman, (await run.FormDecisionAsync()).Delivery);
        await workbench.ShowsAsync(() => OpenWorkbench.Entry(conversation, "FormCardViewModel") is { } card && card["AwaitsYou"].Value<bool>());

        await run.Ui.RunAsync(() => OpenWorkbench.Entry(conversation, "FormCardViewModel")!.Value.ExecuteAsync("SubmitCommand"));

        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        await workbench.ShowsAsync(() => OpenWorkbench.Texts(conversation, "MessageViewModel", "Text").Contains("Going with Database: PostgreSQL"));
        Assert.Equal("Answered: PostgreSQL", await run.Ui.ReadAsync(() => OpenWorkbench.Entry(conversation, "FormCardViewModel")!.Value["Verdict"].Text));
    }

    [Fact]
    public async Task TheSidebarMovesAJobBetweenGroupsAsItIsInterruptedAndContinuedAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "hang");
        var workbench = await run.WorkbenchAsync();
        var conversation = await workbench.SelectAsync(run.Job);
        await run.ResumableAsync();
        await workbench.ShowsInGroupAsync(run.Job, "Running", "working");

        await run.Ui.RunAsync(() => conversation["Composer"].ExecuteAsync("InterruptCommand"));
        Assert.Equal(HoldReason.Interrupted, (await run.HoldAsync()).Reason);
        Assert.Equal(JobStatus.NeedsHelp, await run.SettledAsync());
        await workbench.ShowsInGroupAsync(run.Job, "NeedsYou", "interrupted");

        await run.Ui.RunAsync(() =>
        {
            conversation["Composer"].Set("Draft", "Carry on where you stopped.");

            return conversation["Composer"].ExecuteAsync("SendCommand");
        });
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        await workbench.ShowsInGroupAsync(run.Job, "ReadyForReview");

        await workbench.ShowsAsync(() => OpenWorkbench.Texts(conversation, "PromptViewModel", "Origin").Count == 2);
        Assert.Equal(["Instruction", "You"], await run.Ui.ReadAsync(() => OpenWorkbench.Texts(conversation, "PromptViewModel", "Origin")));
    }
}
