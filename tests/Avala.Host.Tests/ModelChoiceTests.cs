using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Delegation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Host.Tests;

public sealed class ModelChoiceTests(PublishedPlugins plugins)
{
    private const string Connections = """
        {
          "connections": [
            { "name": "first", "provider": "simulator" },
            { "name": "second", "provider": "simulator-second" },
            { "name": "wrong", "provider": "simulator", "settings": { "model": "huge" } }
          ]
        }
        """;

    private static ConnectionName First => new("first");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AJobChosenOnTheNewJobPageRunsWithItsModelAndEffortAndTheHeaderAndInspectorShowThemAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, [("connections.json", Connections)], []);
        var activity = run.Watch<AgentActivity>();
        var page = run.Page("New job");
        await run.Ui.RunAsync(() =>
        {
            ((Sdk.IActivatable)page.Target).Activate();

            return page["Loading"].Value<Task>();
        });

        var offered = await run.Ui.RunAsync(async () =>
        {
            page.Set("Repository", run.Repository.Path);
            await page["Previewing"].Value<Task>();
            page.Set("Connection", "first");
            await page["Offering"].Value<Task>();

            return (page["Models"]["Models"].Items.Select(model => model.Text).ToList(), page["Models"]["Efforts"].Items.Select(effort => effort.Text).ToList());
        });
        await run.Ui.RunAsync(() =>
        {
            page["Models"].Set("Model", "simulated-large");
            page["Models"].Set("Effort", "high");
            page.Set("Instruction", SimulatedRun.Simulate("reply"));

            return page.ExecuteAsync("SubmitCommand");
        });
        var job = Outcomes.Present(await run.Ui.ReadAsync(() => page["LastSubmitted"].Value<Option<JobId>>()));

        var ran = (ModelReported)(await activity.UntilAsync(update => update.Event is ModelReported)).Event;
        Assert.Equal(["Default (simulated-medium)", "simulated-large", "simulated-medium", "simulated-small"], offered.Item1);
        Assert.Equal(["Default (medium)", "low", "medium", "high"], offered.Item2);
        Assert.Equal(("simulated-large", Option<string>.Some("high")), (ran.Model, ran.Effort));
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(job));
        var workbench = await run.WorkbenchAsync();
        var conversation = await workbench.SelectAsync(job);
        var section = workbench.Section("AutonomySectionViewModel");
        await run.Ui.InvokeAsync(() => workbench.Page.Execute("ToggleInspectorCommand"), Cancellation);
        await run.Ui.PresentedAsync(section.Presentation, () => section["IsLoaded"].Value<bool>(), () => $"{section.Kind} did not load");
        Assert.EndsWith("· first · simulated-large · high effort", await run.Ui.ReadAsync(() => conversation["Place"].Text), StringComparison.Ordinal);
        Assert.Equal("simulated-large · high effort", await run.Ui.ReadAsync(() => section["Model"].Text));
    }

    [Fact]
    public async Task AJobOnTheSecondHarnessRunsWithItsDefaultAndNoEffortAsync()
    {
        await using var run = await SimulatedRun.ConnectedAsync(plugins, "reply", new ConnectionName("second"), [("connections.json", Connections)]);

        var ran = Assert.Single((await run.TurnAsync()).OfType<ModelReported>());

        Assert.Equal(("second-fast", Option<string>.None), (ran.Model, ran.Effort));
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
    }

    [Fact]
    public async Task AConnectionWhoseSettingNamesAModelItsHarnessDoesNotOfferIsRefusedBeforeAnySessionOpensAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, [("connections.json", Connections)], []);

        var catalog = await run.Get<IConnections>().CatalogAsync(Cancellation);
        var checkedOne = await run.Get<IConnections>().CheckAsync(new ConnectionName("wrong"), Cancellation);
        var submitted = await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("reply")) { Connection = new ConnectionName("wrong") });
        var unoffered = await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("reply")) { Connection = First, Model = new ModelChoice("second-fast", Option<string>.None) });

        Assert.Equal(Option<ConnectionError>.Some(ConnectionError.UnofferedModel), catalog.Connections.Single(connection => connection.Name.Value == "wrong").Problem);
        Assert.Equal(ConnectionError.UnofferedModel, Outcomes.FailsWith(checkedOne));
        Assert.Equal(JobRejection.UnofferedModel, Outcomes.FailsWith(submitted));
        Assert.Equal(JobRejection.UnofferedModel, Outcomes.FailsWith(unoffered));
        Assert.Empty(await run.Get<IJobCatalog>().ListAsync(Cancellation));
    }

    [Fact]
    public async Task ARepositorysJobFileChoosesTheModelOfAJobThatNamesNoneAsync()
    {
        await using var run = await SimulatedRun.ConnectedAsync(
            plugins,
            "reply",
            First,
            [("connections.json", Connections)],
            (".avala/jobs.json", """{ "model": "simulated-small" }"""));

        var ran = Assert.Single((await run.TurnAsync()).OfType<ModelReported>());

        Assert.Equal(("simulated-small", Option<string>.Some("medium")), (ran.Model, ran.Effort));
    }

    [Fact]
    public async Task AnOrchestratorGivesItsChildTheModelItAsksForAndAnUnofferedOneIsRefusedAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(
            plugins,
            [("connections.json", Connections)],
            [
                (".avala/permissions.json", """{ "autonomy": "autonomous" }"""),
                (".avala/jobs.json", """{ "delegation": { "connections": ["first"] } }"""),
            ]);
        var refused = run.Watch<DelegationRefused>();
        var delegated = run.Watch<ChildDelegated>();
        var activity = run.Watch<AgentActivity>();
        var started = run.Watch<JobSessionStarted>();
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, "[simulate: delegate-model] Write the notes") { Connection = First }));

        var refusal = (await refused.UntilAsync(_ => true)).Delegation;
        var child = Outcomes.Present((await delegated.UntilAsync(_ => true)).Delegation.Child);
        var session = (await started.UntilAsync(announced => announced.Job == child)).Session;
        var ran = (ModelReported)(await activity.UntilAsync(update => update.Event is ModelReported reported && reported.Session == session)).Event;

        Assert.Equal(Option<DelegationError>.Some(DelegationError.UnofferedModel), refusal.Refusal);
        Assert.Equal(new ModelChoice("simulated-small", "low"), Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(child, Cancellation)).Summary.Model);
        Assert.Equal(("simulated-small", Option<string>.Some("low")), (ran.Model, ran.Effort));
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(job));
    }
}
