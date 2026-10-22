using Avala.Jobs.Contracts;
using Avala.Testing;

namespace Avala.Host.Tests;

public sealed class HarnessRetryTests(PublishedPlugins plugins)
{
    [Fact]
    public async Task AHarnessThatRetriesARequestAndThenGoesOnShowsInTheConversationThatItRecoveredAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "retry");
        var workbench = await run.WorkbenchAsync();
        var conversation = await workbench.SelectAsync(run.Job);

        await workbench.ShowsAsync(() => Retries(conversation).Count == 1 && Retries(conversation)[0].Contains("recovered", StringComparison.Ordinal));

        Assert.Equal(["The harness recovered after 2 retries (overloaded)."], await run.Ui.ReadAsync(() => Retries(conversation)));
    }

    [Fact]
    public async Task ARealClaudeCodeSessionThatRetriedTwiceShowsInTheConversationThatTheHarnessRecoveredAsync()
    {
        using var claude = await TranscribedClaude.PrepareAsync("api-retry");
        await using var run = await SimulatedRun.TranscribedAsync(plugins, claude.Plugin, new JobRequest(string.Empty, "Reply with the single word ready."), claude.Data, []);
        var workbench = await run.WorkbenchAsync();
        var conversation = await workbench.SelectAsync(run.Job);

        await workbench.ShowsAsync(() => Retries(conversation).Any(note => note.Contains("recovered", StringComparison.Ordinal)));

        Assert.Equal(["The harness recovered after 2 retries (overloaded)."], await run.Ui.ReadAsync(() => Retries(conversation)));
    }

    private static List<string> Retries(Bound conversation) =>
        [.. conversation["Entries"].Items.Where(entry => entry.Kind == "RetryViewModel").Select(entry => entry["Note"].Text)];
}
