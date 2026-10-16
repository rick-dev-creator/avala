using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Testing;

namespace Avala.Host.Tests;

public sealed class DelegationPaneTests(PublishedPlugins plugins)
{
    private static readonly (string File, string Content)[] Login =
    [
        ("connections.json", """{ "default": "work", "connections": [ { "name": "work", "provider": "simulator", "credential": { "source": "login" } } ] }"""),
        ("connections/work/.login", string.Empty),
    ];

    [Fact]
    public async Task TheDelegationPaneShowsTheOrchestratorMarkedAboveItsChildWithEachJobsSubagentsAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(
            plugins,
            Login,
            [
                (".avala/permissions.json", """{ "autonomy": "autonomous" }"""),
                (".avala/jobs.json", """{ "delegation": { "connections": ["work"], "maxChildren": 1 } }"""),
            ]);
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, "[simulate: delegate-agents] Ship the release")));
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(job));
        var workbench = await run.WorkbenchAsync();
        _ = await workbench.SelectAsync(job);
        var header = workbench.Page["Regions"]["Header"].Items;
        var toggle = header.Single(item => item.Kind == "DelegationToggleViewModel");
        var pane = workbench.Page["Regions"]["Preview"].Items.Single(item => item.Kind == "DelegationPaneViewModel");
        await run.Ui.PresentedAsync(toggle.Presentation, () => toggle["IsShown"].Value<bool>(), () => "toggle hidden");

        await run.Ui.InvokeAsync(() => toggle.Execute("ToggleCommand"), TestContext.Current.CancellationToken);
        await run.Ui.PresentedAsync(pane.Presentation, () => pane["Children"].Items.Count == 1 && pane.Has("Root"), () => "pane empty");

        var shown = await run.Ui.ReadAsync(() => (
            pane["IsOpen"].Value<bool>(),
            workbench.Page["Regions"]["IsPreviewOpen"].Value<bool>(),
            (pane["Root"]["Title"].Text, pane["Root"]["IsViewing"].Value<bool>()),
            pane["Root"]["Subagents"].Items.Select(agent => agent["Name"].Text).ToList(),
            pane["Children"].Items[0]["IsViewing"].Value<bool>(),
            pane["Children"].Items[0]["Subagents"].Items.Select(agent => (agent["Name"].Text, agent["StateText"].Text)).ToList()));
        Assert.True(shown.Item1);
        Assert.True(shown.Item2);
        Assert.Equal(("[simulate: delegate-agents] Ship the release", true), shown.Item3);
        Assert.Equal(["Plan the split"], shown.Item4);
        Assert.False(shown.Item5);
        Assert.Equal([("Read the changelog", "done")], shown.Item6);
        Assert.True(await run.Ui.ReadAsync(() => toggle["IsOpen"].Value<bool>()));
    }

    [Fact]
    public async Task ASubagentStartedWhileItsJobIsShownOpensThePaneWithItsCommandAndUsageWhileItsToolsStayOutOfTheConversationAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "subagents");
        var install = await run.DecisionAsync();
        var workbench = await run.WorkbenchAsync();
        var conversation = await workbench.SelectAsync(run.Job);
        var pane = workbench.Page["Regions"]["Preview"].Items.Single(item => item.Kind == "DelegationPaneViewModel");
        var closed = await run.Ui.ReadAsync(() => pane["IsOpen"].Value<bool>());

        Outcomes.Succeeds(await run.Get<IAgents>().RespondAsync(install.Session, new PermissionDecision(install.Item, PermissionAnswer.Allow), TestContext.Current.CancellationToken));
        var build = await run.DecisionAsync();
        await run.Ui.PresentedAsync(
            pane.Presentation,
            () => pane["IsOpen"].Value<bool>() && pane.Has("Root") && pane["Root"]["Subagents"].Items.Count == 1 && pane["Root"]["Subagents"].Items[0]["Meter"].Text.Length > 0,
            () => "no sub-agent in the pane");

        var agent = await run.Ui.ReadAsync(() => pane["Root"]["Subagents"].Items[0]);
        var shown = await run.Ui.ReadAsync(() => (agent["Name"].Text, agent["Activity"].Text, agent["Meter"].Text));
        var rows = await run.Ui.ReadAsync(() => conversation["Entries"].Items.Where(entry => entry.Kind == "ToolViewModel").Select(tool => tool["Title"].Text).ToList());
        Assert.False(closed);
        Assert.Equal("survey-build", build.Item.Value);
        Assert.Equal(("Survey the greeting", "Run dotnet build", "3 tool uses · 3,600 tokens"), shown);
        Assert.DoesNotContain(rows, title => title.Contains("greeting", StringComparison.Ordinal) && !title.StartsWith("Subagent", StringComparison.Ordinal));
        Assert.DoesNotContain(rows, title => title.Contains("dotnet build", StringComparison.Ordinal));
    }
}
