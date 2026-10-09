using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Testing;
using Avala.Transcripts.Contracts;

namespace Avala.Host.Tests;

public sealed class ConversationRestartTests(PublishedPlugins plugins)
{
    private const string KeptNote = "RestartViewModel | Avala restarted. Everything above happened before the restart.";

    private static readonly string[] Shown = ["Attempt", "Origin", "Text", "Title", "Input", "Output", "Progress", "Summary", "Tokens", "Cost", "Target", "Note"];

    [Fact]
    public async Task AFinishedJobsConversationReadsTheSameAfterARestartFollowedByTheRestartNoteAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "edit", (".avala/permissions.json", ReviewTests.TestsPolicy));
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        var before = await ConversationAsync(run, entries => entries.Contains("TurnEndViewModel"));
        await run.DeliveredAsync();

        await run.RestartAsync();
        await run.StartedAsync();

        var after = await ConversationAsync(run, entries => entries.Contains("RestartViewModel"));
        Assert.Contains(before, entry => entry.StartsWith("ToolViewModel | ", StringComparison.Ordinal) && entry.Contains("Passed: 12, Failed: 0", StringComparison.Ordinal));
        Assert.Contains(before, entry => entry.StartsWith("PlanViewModel | ", StringComparison.Ordinal));
        Assert.Equal([.. before, KeptNote], after);
    }

    [Fact]
    public async Task AJobRestartedWhileItWaitedShowsEverythingBeforeTheRestartThenTheNoteThenWhatItDidNextAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "waiting-permission");
        Assert.Equal(DecisionDelivery.LeftToHuman, (await run.DecisionAsync()).Delivery);
        await run.ResumableAsync();
        var before = await ConversationAsync(run, entries => entries.Contains("PermissionCardViewModel"));
        await run.DeliveredAsync();

        await run.RestartAsync();

        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        var after = await ConversationAsync(run, entries => entries.Count(entry => entry == "TurnEndViewModel") == 1);
        Assert.Contains(before, entry => entry.StartsWith("ReasoningViewModel | The schema changed, so the database needs a migration.", StringComparison.Ordinal));
        Assert.Equal([.. before, KeptNote], after.Take(before.Count + 1));
        Assert.Contains(after.Skip(before.Count + 1), entry => entry.StartsWith("MessageViewModel | I left the migration for you to run.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ADiscardedJobsConversationIsReleasedWithItsWorktreeSoAfterARestartItShowsOnlyItsPromptAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "reply");
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        var before = await ConversationAsync(run, entries => entries.Contains("TurnEndViewModel"));
        Outcomes.Succeeds(await run.Get<IJobs>().DiscardAsync(run.Job, TestContext.Current.CancellationToken));
        Assert.Equal(run.Job, (await run.ReclaimedAsync()).Job);
        await run.DeliveredAsync();

        await run.RestartAsync();
        await run.StartedAsync();

        var kept = await run.Get<ITranscripts>().EarlierRunsAsync(run.Job, TestContext.Current.CancellationToken);
        var after = await ConversationAsync(run, entries => entries.Contains("RestartViewModel"));
        Assert.Contains(before, entry => entry.StartsWith("MessageViewModel | ", StringComparison.Ordinal));
        Assert.Equal([new AttemptBegan(1)], kept.Select(fact => fact.Fact));
        Assert.Equal([before[0], KeptNote], after);
    }

    private static async Task<IReadOnlyList<string>> ConversationAsync(SimulatedRun run, Func<IReadOnlyList<string>, bool> shown)
    {
        var workbench = await run.WorkbenchAsync();
        var conversation = await workbench.SelectAsync(run.Job);
        await workbench.ShowsAsync(() => shown(OpenWorkbench.Kinds(conversation)));

        return await run.Ui.ReadAsync<IReadOnlyList<string>>(() => [.. conversation["Entries"].Items.Select(Describe)]);
    }

    private static string Describe(Bound entry) =>
        string.Join(" | ", [entry.Kind, .. Shown.Where(entry.Has).Select(property => entry[property].Text)]);
}
