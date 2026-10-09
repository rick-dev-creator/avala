using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Sdk.Regions;
using Avala.Shell;
using Avala.Testing;

namespace Avala.Host.Tests;

public sealed class ScreenHonestyTests(PublishedPlugins plugins)
{
    private const string Autonomous = """{ "autonomy": "autonomous" }""";

    private const string TwoHarnesses = """
        {
          "connections": [
            { "name": "one", "provider": "simulator" },
            { "name": "other", "provider": "simulator-second" }
          ]
        }
        """;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheUsagePageSaysItShowsAllRecordedUsageAndStillShowsAnEarlierRunsCostAfterARestartAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "reply");
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        var before = await CostAsync(run);

        await run.RestartAsync();
        var after = await CostAsync(run);

        Assert.Equal(("All recorded usage, kept across restarts", before.Cost), after);
        Assert.NotEqual("no usage yet", before.Cost);
    }

    [Fact]
    public async Task TheResourcesPageAndItsIndicatorSayTheyMeasureAvalasAgentsNotTheComputerAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, [], []);
        var resources = await ActivatedAsync(run, "Resources");
        var indicator = new Bound(Assert.Single(run.Get<IEnumerable<RegionContribution>>(), contribution => contribution.Region == ShellRegions.SidebarFooter && contribution.Order == 0).ViewModel);

        Assert.Equal(
            ("Avala's agents and worktrees, not the whole computer", "Avala's agents"),
            await run.Ui.ReadAsync(() => (resources["Scope"].Text, indicator["Scope"].Text)));
    }

    [Fact]
    public async Task DontAskAgainChosenInThePopoverAnswersTheNextIdenticalRequestOfThatSessionOnlyAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "repeated-permission");
        Assert.Equal(DecisionDelivery.LeftToHuman, (await run.DecisionAsync()).Delivery);
        var workbench = await run.WorkbenchAsync();
        var decisions = await run.Ui.ReadAsync(() => workbench.Toolbar["Decisions"]);
        await workbench.ShowsAsync(() => decisions["Items"].Items.Count == 1);

        var label = await run.Ui.ReadAsync(() =>
        {
            var decision = decisions["Items"].Items[0];
            decision.Set("DontAskAgain", true);

            return decision["DontAskAgainLabel"].Text;
        });
        await run.Ui.RunAsync(() => decisions.ExecuteAsync("AnswerCommand"));
        var second = await run.DecisionAsync();

        Assert.Equal("Don't ask again this session", label);
        Assert.Equal((DecisionDelivery.Answered, "don't ask again this session"), (second.Delivery, Outcomes.Present(second.Rule).Name));
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
    }

    [Fact]
    public async Task ANewJobShowsItsRepositorysAutonomyAndRunsSupervisedWhenChosenAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, [], [(".avala/permissions.json", Autonomous)]);
        var page = await ActivatedAsync(run, "New job");

        var offered = await run.Ui.RunAsync(async () =>
        {
            page.Set("Repository", run.Repository.Path);
            await page["Previewing"].Value<Task>();

            return (string.Join(" | ", page["Autonomies"].Items.Select(item => item.Text)), page["Autonomy"].Text, page["AutonomyNote"].Text.Split(':')[0]);
        });
        await run.Ui.RunAsync(() =>
        {
            page.Set("Autonomy", "Supervised");
            page.Set("Instruction", SimulatedRun.Simulate("reply"));

            return page.ExecuteAsync("SubmitCommand");
        });
        var applied = await run.AutonomyAsync();

        Assert.Equal(("Repository's level: autonomous | Supervised", "Repository's level: autonomous", "Autonomous, as the repository's .avala/permissions.json declares"), offered);
        Assert.Equal((Autonomy.Autonomous, Autonomy.Supervised), (applied.Declared, applied.Effective));
    }

    [Fact]
    public async Task OutsideDeveloperModeTheSimulatorOffersNoConnectionOfItsOwnAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, [("simulated-logins/one/.login", string.Empty)], [], developer: false);
        var page = await ActivatedAsync(run, "New job");

        var catalog = await run.Get<IConnections>().CatalogAsync(Cancellation);

        Assert.Empty(catalog.Connections);
        Assert.Equal(["Auto"], await run.Ui.ReadAsync(() => page["Connections"].Items.Select(item => item.Text).ToList()));
    }

    [Fact]
    public async Task OutsideDeveloperModeASimulatorConnectionDeclaredInConnectionsJsonIsOfferedAndRunsAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(
            plugins,
            [("connections.json", """{ "connections": [ { "name": "demo", "provider": "simulator" } ] }"""), ("simulated-logins/one/.login", string.Empty)],
            [],
            developer: false);

        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("reply"))));

        Assert.Equal([new ConnectionName("demo")], (await run.Get<IConnections>().CatalogAsync(Cancellation)).Connections.Select(connection => connection.Name));
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(job));
    }

    [Fact]
    public async Task AutoComparesTheConnectionsOfEveryProviderAndSaysWhenNothingHasBeenReadYetAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, [("connections.json", TwoHarnesses)], []);
        var page = await ActivatedAsync(run, "New job");
        var unread = await RouteAsync(run, page);
        var recorded = run.Watch<Avala.Observability.Contracts.UsageRecorded>();
        var spent = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("near-limit")) { Connection = new ConnectionName("one") }));
        var reports = 0;
        _ = await recorded.UntilAsync(_ => ++reports == 2);
        _ = await run.SettledAsync(spent);
        var chosen = run.Watch<ConnectionChosen>();
        var opened = run.Watch<Avala.Agents.Contracts.SessionOpened>();

        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("reply"))));
        var choice = (await chosen.UntilAsync(announced => announced.Job == job)).Choice;
        var session = await opened.UntilAsync(announced => announced.Connection == new ConnectionName("other"));

        Assert.Equal("Auto → one · no connection has reported usage yet, so there is no capacity to compare: the first usable connection", unread);
        Assert.Equal(
            (new ConnectionName("other"), ChoiceReason.MostCapacity, 2),
            (choice.Connection, choice.Reason, choice.Compared.Count));
        Assert.Equal("simulator-second", session.Provider.Id);
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(job));
    }

    [Fact]
    public async Task EditingMissingSettingsFilesCreatesValidOnesFromTheirTemplatesAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, [], []);
        var settings = await ActivatedAsync(run, "Settings");
        var (machine, repository) = await run.Ui.ReadAsync(() => (settings["Machine"], settings["Repository"]));

        await run.Ui.RunAsync(() => machine.ExecuteAsync("OpenConnectionsCommand"));
        await run.Ui.RunAsync(async () =>
        {
            repository.Set("Repository", run.Repository.Path);
            await repository.ExecuteAsync("ReadCommand");

            foreach (var file in repository["Files"].Items)
            {
                await repository.ExecuteAsync("EditCommand", file.Target);
            }
        });
        Assert.All(
            [".avala/permissions.json", ".avala/budget.json", ".avala/checks.json", ".avala/jobs.json"],
            file => Assert.True(File.Exists(Path.Combine(run.Repository.Path, file)), file));
        var permissions = Path.Combine(run.Repository.Path, ".avala/permissions.json");
        await run.Repository.CommitAsync(".avala/permissions.json", await File.ReadAllTextAsync(permissions, Cancellation), Cancellation);

        await run.Ui.RunAsync(() => repository.ExecuteAsync("ReadCommand"));

        Assert.Equal(
            ("Applied", "connections.json did not exist, so it was created in the data folder with the Auto default. Connections you declare in it apply once Avala starts again."),
            await run.Ui.ReadAsync(() => (machine["ConnectionsFile"].Text, machine["Notice"].Text)));
        Assert.Contains("\"default\": \"auto\"", await File.ReadAllTextAsync(Path.Combine(run.DataFolder, "connections.json"), Cancellation), StringComparison.Ordinal);
        Assert.Equal(
            ["Applied", "Applied", "Applied", "Declared"],
            await run.Ui.ReadAsync(() => repository["Files"].Items.Select(file => file["Status"].Text).ToList()));
    }

    [Fact]
    public async Task AFormWithFreeTextSeveralChoicesAndAConfirmationIsAnsweredFromThePopoverAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "fields");
        Assert.Equal(DecisionDelivery.LeftToHuman, (await run.FormDecisionAsync()).Delivery);
        var workbench = await run.WorkbenchAsync();
        var decisions = await run.Ui.ReadAsync(() => workbench.Toolbar["Decisions"]);
        await workbench.ShowsAsync(() => decisions["Items"].Items.Count == 1);

        var shown = await run.Ui.ReadAsync(() =>
        {
            var decision = decisions["Items"].Items[0];
            var blank = (decision["HasFields"].Value<bool>(), decision["Options"].Items.Count, ((System.Windows.Input.ICommand)decisions["AnswerCommand"].Target).CanExecute(null));
            decision["Fields"].Items[0].Set("Text", "v1.4.0");
            decision["Fields"].Items[2].Set("Confirmed", true);

            return blank;
        });
        await run.Ui.RunAsync(() => decisions.ExecuteAsync("AnswerCommand"));

        Assert.Equal((true, 0, true), shown);
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        var conversation = await workbench.SelectAsync(run.Job);
        await workbench.ShowsAsync(() => OpenWorkbench.Texts(conversation, "MessageViewModel", "Text").Contains("Going with Tag: v1.4.0; Targets: NuGet; Notes: approved"));
    }

    [Fact]
    public async Task ThePopoverTellsHowLongADecisionHasWaitedAsTheClockMovesAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "question");
        Assert.Equal(DecisionDelivery.LeftToHuman, (await run.FormDecisionAsync()).Delivery);
        var workbench = await run.WorkbenchAsync();
        var decisions = await run.Ui.ReadAsync(() => workbench.Toolbar["Decisions"]);
        await workbench.ShowsAsync(() => decisions["Items"].Items.Count == 1);
        var fresh = await run.Ui.ReadAsync(() => decisions["Items"].Items[0]["Waiting"].Text);

        await run.DeliveredAsync();
        run.Clock.Advance(TimeSpan.FromMinutes(5));

        await workbench.ShowsAsync(() => decisions["Items"].Items[0]["Waiting"].Text == "5m");
        Assert.Equal("<1m", fresh);
    }

    [Fact]
    public async Task AThoughtTheHarnessDidNotShareSaysSoAndCannotBeOpenedAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "unshared-thought");
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        var workbench = await run.WorkbenchAsync();
        var conversation = await workbench.SelectAsync(run.Job);
        await workbench.ShowsAsync(() => OpenWorkbench.Entry(conversation, "ReasoningViewModel") is { } thought && !thought["IsThinking"].Value<bool>());

        var thought = await run.Ui.ReadAsync(() =>
        {
            var entry = OpenWorkbench.Entry(conversation, "ReasoningViewModel")!.Value;

            return (entry["Summary"].Text.EndsWith(" · content not shared by the harness", StringComparison.Ordinal), entry["HasText"].Value<bool>(), ((System.Windows.Input.ICommand)entry["ToggleCommand"].Target).CanExecute(null));
        });

        Assert.Equal((true, false, false), thought);
    }

    [Fact]
    public async Task TheSidebarRowCountsTheDecisionsWaitingOnItsJobAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "permission");
        Assert.Equal(DecisionDelivery.LeftToHuman, (await run.DecisionAsync()).Delivery);
        var workbench = await run.WorkbenchAsync();

        await workbench.ShowsAsync(() => Row(workbench, run.Job) is { } row && row["PendingDecisions"].Value<int>() == 1 && row["HasPendingDecisions"].Value<bool>());
        var decisions = await run.Ui.ReadAsync(() => workbench.Toolbar["Decisions"]);
        await run.Ui.RunAsync(() => decisions.ExecuteAsync("AnswerCommand"));

        await workbench.ShowsAsync(() => Row(workbench, run.Job) is { } row && !row["HasPendingDecisions"].Value<bool>());
    }

    [Fact]
    public async Task TheOverviewCardOfAConnectionListsItsLimitWindowsAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "spent-window", (".avala/budget.json", """{ "holdAtLimit": 0.9 }"""));
        Assert.Equal(JobStatus.NeedsHelp, await run.SettledAsync());
        var connections = (await ActivatedAsync(run, "Overview"))["Connections"];

        await run.Ui.PresentedAsync(
            connections.Presentation,
            () => connections["Connections"].Items is [var card] && card["Limits"].Items.Count == 1,
            () => string.Join(", ", connections["Connections"].Items.Select(card => $"{card["Name"].Text} with {card["Limits"].Items.Count} limits")));

        Assert.Equal(
            ("5h", "95%"),
            await run.Ui.ReadAsync(() => (connections["Connections"].Items[0]["Limits"].Items[0]["Window"].Text, connections["Connections"].Items[0]["Limits"].Items[0]["UsedText"].Text)));
    }

    private static readonly string[] Groups = ["NeedsYou", "Running", "ReadyForReview", "Done"];

    private static Bound? Row(OpenWorkbench workbench, JobId job) =>
        Groups
            .SelectMany(group => workbench.Sidebar[group].Items)
            .Cast<Bound?>()
            .FirstOrDefault(row => row!.Value["Job"].Value<JobId>() == job);

    private static async Task<(string Scope, string Cost)> CostAsync(SimulatedRun run)
    {
        var usage = await ActivatedAsync(run, "Usage");
        await run.Ui.PresentedAsync(usage.Presentation, () => usage["Connections"].Items.Count == 1, () => $"{usage["Connections"].Items.Count} connections");

        return await run.Ui.ReadAsync(() => (usage["Scope"].Text, usage["Connections"].Items[0]["Cost"].Text));
    }

    private static async Task<string> RouteAsync(SimulatedRun run, Bound page) =>
        await run.Ui.RunAsync(async () =>
        {
            page.Set("Repository", run.Repository.Path);
            await page["Previewing"].Value<Task>();

            return page["Route"].Text;
        });

    private static async Task<Bound> ActivatedAsync(SimulatedRun run, string title)
    {
        var page = run.Page(title);
        await run.Ui.RunAsync(() =>
        {
            ((IActivatable)page.Target).Activate();

            return page.Has("Loading") ? page["Loading"].Value<Task>() : Task.CompletedTask;
        });

        return page;
    }
}
