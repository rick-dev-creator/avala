using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;
using Avala.Testing;

namespace Avala.Host.Tests;

public sealed class MidTurnNoticeTests(PublishedPlugins plugins)
{
    private const string Autonomous = """{ "autonomy": "autonomous" }""";

    private const string Allows = "Avala now allows ";

    private const string Stopped = "Fetched the greetings once Avala allowed the request.";

    [Fact]
    public async Task TwoIdenticalAllowNoticesInARowReachTheAgentOnceAsync()
    {
        using var claude = await TranscribedClaude.PrepareAsync("repeated-denial");
        await using var run = await SimulatedRun.TranscribedAsync(
            plugins,
            claude.Plugin,
            new JobRequest(string.Empty, "Fetch the greetings page."),
            claude.Data,
            [(".avala/permissions.json", Autonomous)]);
        var workbench = await run.WorkbenchAsync();
        var conversation = await workbench.SelectAsync(run.Job);
        await workbench.ShowsAsync(() => Running(conversation) && Allowable(conversation).Count == 2);

        await run.Ui.RunAsync(() => Allowable(conversation)[0].ExecuteAsync("AllowCommand"));
        await run.Ui.RunAsync(() => Allowable(conversation) is [var left, ..] ? left.ExecuteAsync("AllowCommand") : Task.CompletedTask);
        await workbench.ShowsAsync(() => OpenWorkbench.Texts(conversation, "MessageViewModel", "Text").Contains(Stopped));

        var told = await run.Ui.ReadAsync(() => OpenWorkbench.Texts(conversation, "InterjectionViewModel", "Text").Concat(OpenWorkbench.Texts(conversation, "PromptViewModel", "Text")).ToList());
        var heard = told.Sum(text => text.Split(Allows).Length - 1);
        Assert.True(heard == 1, $"The agent was told {heard} times: [{string.Join(" | ", told)}]");
    }

    private static bool Running(Bound conversation) => conversation["Composer"]["Status"].Value<JobStatus>() == JobStatus.Running;

    private static List<Bound> Allowable(Bound conversation) =>
        [.. conversation["Entries"].Items.Where(entry => entry.Kind == "DeniedCardViewModel" && entry["CanAllow"].Value<bool>())];
}
