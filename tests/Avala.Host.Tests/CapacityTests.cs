using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Connections;
using Avala.Delegation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Host.Tests;

public sealed class CapacityTests(PublishedPlugins plugins)
{
    private const string PassingChecks = """{ "checks": [ { "name": "git", "command": "git", "arguments": ["--version"], "timeoutSeconds": 60 } ] }""";

    private const string HoldNearTheLimit = """{ "holdAtLimit": 0.9 }""";


    private static readonly (string File, string Content)[] TwoAccounts =
    [
        ("simulated-logins/one/.login", string.Empty),
        ("simulated-logins/two/.login", string.Empty),
    ];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static ConnectionName One => new("simulator-one");

    private static ConnectionName Two => new("simulator-two");

    [Fact]
    public async Task TwoSimulatedAccountsAreDiscoveredWithoutAConnectionsFileAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, TwoAccounts, []);

        var catalog = await run.Get<IConnections>().CatalogAsync(Cancellation);

        Assert.Equal((ConnectionFileStatus.Absent, Option<ConnectionName>.Some(One)), (catalog.File, catalog.Default));
        Assert.Equal(
            [(One, "simulator", ConnectionOrigin.Discovered), (Two, "simulator", ConnectionOrigin.Discovered)],
            catalog.Connections.Select(connection => (connection.Name, connection.Provider, connection.Origin)));
    }

    [Fact]
    public async Task AJobNamingNoConnectionRunsOnTheFirstDiscoveredAccountWhileNeitherHasReadingsAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, TwoAccounts, []);
        var chosen = run.Watch<ConnectionChosen>();

        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("reply"))));
        var choice = (await chosen.UntilAsync(announced => announced.Job == job)).Choice;

        Assert.Equal((One, ChoiceReason.MostCapacity), (choice.Connection, choice.Reason));
        Assert.Equal([(One, 0.0, true), (Two, 0.0, true)], choice.Compared.Select(candidate => (candidate.Connection, candidate.Used, candidate.Available)));
        Assert.Equal(JobStatus.AwaitingReview, Assert.Single(await run.SettledAsync(job)));
        Assert.Equal(One, await RanOnAsync(run, job));
    }

    [Fact]
    public async Task WhenAnAccountIsNearItsLimitTheNextJobNamingNoConnectionGoesToTheOtherAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, TwoAccounts, []);
        await NearTheLimitAsync(run, One);
        var chosen = run.Watch<ConnectionChosen>();

        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("reply"))));
        var choice = (await chosen.UntilAsync(announced => announced.Job == job)).Choice;

        Assert.Equal(Two, choice.Connection);
        Assert.Equal([(One, 0.95), (Two, 0.0)], choice.Compared.Select(candidate => (candidate.Connection, candidate.Used)));
        Assert.Equal("5h", choice.Compared[0].Window.Match(window => window.Window, () => string.Empty));
        Assert.Equal(JobStatus.AwaitingReview, Assert.Single(await run.SettledAsync(job)));
        Assert.Equal(Two, await RanOnAsync(run, job));
    }

    [Fact]
    public async Task AConnectionTheRepositoryNamesIsUsedEvenNearItsLimitAndTheBudgetHoldsTheJobInsteadOfMovingItAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(
            plugins,
            TwoAccounts,
            [(".avala/budget.json", HoldNearTheLimit), (".avala/jobs.json", """{ "connection": "simulator-one" }""")]);
        await NearTheLimitAsync(run, One);
        var held = run.Watch<JobHeld>();

        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("spent-window"))));
        var hold = (await held.UntilAsync(announced => announced.Hold.Job == job)).Hold;

        Assert.Equal(HoldReason.LimitNearlyReached, hold.Reason);
        Assert.Equal(One, await RanOnAsync(run, job));
    }

    [Fact]
    public async Task DelegatedChildrenGoToTheListedAccountWithCapacityWhileTheirParentStaysOnTheOneItNamedAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(
            plugins,
            TwoAccounts,
            [
                (".avala/checks.json", PassingChecks),
                (".avala/permissions.json", """{ "autonomy": "autonomous" }"""),
                (".avala/jobs.json", """{ "delegation": { "connections": ["simulator-one", "simulator-two"], "maxChildren": 2 } }"""),
            ]);
        await NearTheLimitAsync(run, One);
        var delegated = run.Watch<ChildDelegated>();

        var parent = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, "[simulate: delegate] Ship the release") { Connection = One }));
        var both = await delegated.CollectUntilAsync(started => started.Delegation.Item.Value == "delegate-todo");

        Assert.Equal([Two, Two], both.Select(started => Outcomes.Present(started.Delegation.Connection)));
        Assert.All(both, started => Assert.Equal(
            [(One, 0.95, true), (Two, 0.0, true)],
            Outcomes.Present(started.Delegation.Choice).Compared.Select(candidate => (candidate.Connection, candidate.Used, candidate.Available))));
        Assert.Equal(JobStatus.AwaitingReview, Assert.Single(await run.SettledAsync(parent)));
        var tree = Outcomes.Present(await run.Get<IJobCatalog>().TreeAsync(parent, Cancellation));
        Assert.Equal(Option<ConnectionName>.Some(One), tree.Job.Connection);
        Assert.All(tree.Children, child => Assert.Equal(Option<ConnectionName>.Some(Two), child.Job.Connection));
    }

    [Fact]
    public async Task AJobHeldAtTheLimitOfOneAccountIsContinuedOnTheOtherAsANewConversationWhenAskedAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, TwoAccounts, [(".avala/budget.json", HoldNearTheLimit)]);
        var held = run.Watch<JobHeld>();
        var opened = run.Watch<SessionOpened>();
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("spent-window")) { Connection = One }));
        Assert.Equal(HoldReason.LimitNearlyReached, (await held.UntilAsync(announced => announced.Hold.Job == job)).Hold.Reason);

        var continued = Outcomes.Succeeds(await run.Get<IJobs>().ContinueOnAsync(job, Two, "Go on with the release", Cancellation));

        var session = await opened.UntilAsync(announced => announced.Connection == Two);
        Assert.Equal((session.Session, ContinuedIn.NewConversation), (continued.Session, continued.Conversation));
        Assert.Equal(Option<string>.Some($"simulated-login:{Login(run, "two")}"), session.Account.Map(account => account.Id));
        Assert.Equal(Two, await RanOnAsync(run, job));
    }

    private static async Task NearTheLimitAsync(SimulatedRun run, ConnectionName connection)
    {
        var recorded = run.Watch<Avala.Observability.Contracts.UsageRecorded>();
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("near-limit")) { Connection = connection }));
        var reports = 0;
        _ = await recorded.UntilAsync(_ => ++reports == 2);
        _ = await run.SettledAsync(job);
    }

    private static async Task<ConnectionName> RanOnAsync(SimulatedRun run, JobId job) =>
        Outcomes.Present(Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(job, Cancellation)).Summary.Connection);

    private static string Login(SimulatedRun run, string account) => Path.Combine(run.DataFolder, "simulated-logins", account);
}
