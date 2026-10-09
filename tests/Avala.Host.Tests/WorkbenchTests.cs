using Avala.Jobs.Contracts;

namespace Avala.Host.Tests;

public sealed class WorkbenchTests(PublishedPlugins plugins)
{
    private const string Autonomous = """{ "autonomy": "autonomous" }""";

    private static readonly string[] Groups = ["NeedsYou", "Running", "ReadyForReview", "Done"];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AJobStreamsIntoItsConversationInOrderWithItsThinkingAndToolRowsAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "edit", (".avala/permissions.json", Autonomous));
        var conversation = await OpenAsync(run);

        await run.Ui.UntilAsync(() => conversation["Entries"].Items.Any(entry => entry.Kind == "TurnEndViewModel"));

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
        var conversation = await OpenAsync(run);
        await run.Ui.UntilAsync(() => Card(conversation, "PermissionCardViewModel") is { } card && card["AwaitsYou"].Value<bool>());

        await run.Ui.InvokeAsync(() => Card(conversation, "PermissionCardViewModel")!.Value.Execute("AllowCommand"), Cancellation);

        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        await run.Ui.UntilAsync(() => Messages(conversation).Contains("The database is up to date."));
        Assert.Equal(
            ("Allowed", false, "dotnet ef database update"),
            await run.Ui.ReadAsync(() => Card(conversation, "PermissionCardViewModel")!.Value is var card
                ? (card["Verdict"].Text, card["AwaitsYou"].Value<bool>(), card["Target"].Text)
                : default));
    }

    [Fact]
    public async Task AFormIsAnsweredWithItsRecommendedOptionFromTheConversationAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "question");
        var conversation = await OpenAsync(run);
        await run.Ui.UntilAsync(() => Card(conversation, "FormCardViewModel") is { } card && card["AwaitsYou"].Value<bool>());

        await run.Ui.InvokeAsync(() => Card(conversation, "FormCardViewModel")!.Value.Execute("SubmitCommand"), Cancellation);

        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        await run.Ui.UntilAsync(() => Messages(conversation).Contains("Going with Database: PostgreSQL"));
        Assert.Equal("Answered: PostgreSQL", await run.Ui.ReadAsync(() => Card(conversation, "FormCardViewModel")!.Value["Verdict"].Text));
    }

    [Fact]
    public async Task TheSidebarMovesAJobBetweenGroupsAsItIsInterruptedAndContinuedAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "hang");
        var conversation = await OpenAsync(run);
        await run.Ui.UntilAsync(() => GroupOf(run) == ("Running", "working"));

        await run.Ui.InvokeAsync(() => conversation["Composer"].Execute("InterruptCommand"), Cancellation);
        await run.Ui.UntilAsync(() => GroupOf(run) == ("NeedsYou", "interrupted"));

        await run.Ui.InvokeAsync(
            () =>
            {
                conversation["Composer"].Set("Draft", "Carry on where you stopped.");
                conversation["Composer"].Execute("SendCommand");
            },
            Cancellation);
        await run.Ui.UntilAsync(() => GroupOf(run).Group == "ReadyForReview");

        Assert.Equal(
            ["Instruction", "You"],
            await run.Ui.ReadAsync(() => conversation["Entries"].Items.Where(entry => entry.Kind == "PromptViewModel").Select(prompt => prompt["Origin"].Text).ToList()));
    }

    private static async Task<Bound> OpenAsync(SimulatedRun run)
    {
        var workbench = run.Workbench();
        await run.Ui.UntilAsync(() => Row(workbench, run.Job) is not null);
        await run.Ui.InvokeAsync(() => workbench["Sidebar"].Execute("SelectCommand", Row(workbench, run.Job)!.Value.Target), Cancellation);

        return await run.Ui.ReadAsync(() => workbench["Conversation"]);
    }

    private static (string Group, string Fact) GroupOf(SimulatedRun run)
    {
        var sidebar = run.Workbench()["Sidebar"];
        var group = Groups.Single(name => sidebar[name].Items.Any(row => row["Job"].Value<JobId>() == run.Job));

        return (group, Row(run.Workbench(), run.Job)!.Value["Fact"].Text);
    }

    private static Bound? Row(Bound workbench, JobId job) =>
        Groups.SelectMany(group => workbench["Sidebar"][group].Items).Cast<Bound?>().FirstOrDefault(row => row!.Value["Job"].Value<JobId>() == job);

    private static Bound? Card(Bound conversation, string kind) =>
        conversation["Entries"].Items.Cast<Bound?>().FirstOrDefault(entry => entry!.Value.Kind == kind);

    private static List<string> Messages(Bound conversation) =>
        [.. conversation["Entries"].Items.Where(entry => entry.Kind == "MessageViewModel").Select(message => message["Text"].Text)];
}
