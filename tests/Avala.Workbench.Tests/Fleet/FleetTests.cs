using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Budgets.Contracts;
using Avala.Delegation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Sdk;
using Avala.Workbench.Fleet;
using Avala.Workbench.Following;
using Avala.Workbench.Spending;

namespace Avala.Workbench.Tests.Fleet;

public sealed class FleetTests
{
    private readonly SessionBook sessions = new(new FakeUsage());
    private readonly FakeUsage usage = new();
    private readonly FakeBudgets budgets = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EachConnectionListsItsProviderAccountLimitsAndItsUnfinishedAgentsAsync()
    {
        var fixing = Pages.Summary("Fix the failing test", JobStatus.Running);
        var adding = Pages.Summary("Add an endpoint", JobStatus.Running);
        var ended = Pages.Summary("Update a dependency", JobStatus.Approved, connection: "work");
        var reviewing = Pages.Summary("Rate-limit the login", JobStatus.AwaitingReview, connection: "personal");
        _ = await sessions.OpenAsync("work", fixing.Job, account: "rick@work");
        _ = await sessions.OpenAsync("personal", adding.Job, account: "rick@home");
        var limit = new UsageLimit("5h", 0.92, DateTimeOffset.UnixEpoch.AddHours(5));
        usage.Connections.Add(new ConnectionUsage(new ConnectionName("work"), Pages.Simulator, Pages.Used(1.5m, limit)));
        usage.Connections.Add(new ConnectionUsage(new ConnectionName("simulator"), Pages.Simulator, Pages.Used(0m)));

        var fleet = await new FleetReader(new FakeConnections("work", "personal"), usage, sessions, Pages.Board(fixing, adding, ended, reviewing)).ReadAsync(Cancellation);

        Assert.Equal(
            [
                ("work", "Simulator", true, "rick@work", 1, "Fix the failing test"),
                ("personal", "Simulator", false, "rick@home", 0, "Add an endpoint|Rate-limit the login"),
                ("simulator", "Simulator", false, string.Empty, 0, string.Empty),
            ],
            fleet.Connections.Select(connection => (
                connection.Name.Value,
                connection.Provider,
                connection.IsDefault,
                connection.Account.Match(account => account.Label, () => string.Empty),
                connection.Usage.Match(used => used.Limits.Count, () => 0),
                string.Join('|', connection.Agents.Select(job => job.Summary.Instruction).Order(StringComparer.Ordinal)))));
    }

    [Fact]
    public async Task OnlyRootJobsWithChildrenAreOrchestratorsAsync()
    {
        var catalog = new TreeCatalog();
        var root = Pages.Summary("Ship the release", JobStatus.Running);
        var lone = Pages.Summary("Fix the failing test", JobStatus.Running);
        catalog.Jobs.AddRange([root, lone, Pages.Summary("Write the notes", JobStatus.Running, parent: root.Job)]);

        var orchestrators = await Reader(catalog, new FakeDelegations()).OrchestratorsAsync(Cancellation);

        Assert.Equal([root.Job], orchestrators.Select(job => job.Job));
    }

    [Fact]
    public async Task ADelegationTreeShowsEachChildWithItsConnectionHarnessActivityAndSpendAgainstItsCarveAsync()
    {
        var catalog = new TreeCatalog();
        var delegations = new FakeDelegations();
        var root = Pages.Summary("Ship the release", JobStatus.Running, connection: "work");
        var notes = Pages.Summary("Write the notes", JobStatus.Approved, connection: "work", parent: root.Job);
        var todo = Pages.Summary("Update the todo", JobStatus.Running, connection: "personal", parent: root.Job);
        var nested = Pages.Summary("Check the links", JobStatus.Running, connection: "personal", parent: todo.Job);
        catalog.Jobs.AddRange([root, notes, todo, nested]);
        _ = await sessions.OpenAsync("personal", todo.Job);
        usage.Jobs[todo.Job] = Pages.Used(0.4m);
        budgets.Carves[todo.Job] = new BudgetCarve(root.Job, todo.Job, [new Cost(1m, "USD")], Option<long>.None, 0.5, DateTimeOffset.UnixEpoch);
        var call = new DelegationRecord(SessionId.New(), new ItemId("delegate-notes"), "Write the notes", DateTimeOffset.UnixEpoch) { Parent = root.Job };
        delegations.Records.Add(call with { Child = notes.Job, Report = new ChildReport(notes.Job, ChildOutcome.Integrated, JobStatus.Approved, DateTimeOffset.UnixEpoch) });
        delegations.Records.Add(call with { Item = new ItemId("delegate-deep"), Instruction = "Go deeper", Refusal = DelegationError.DepthExceeded });

        var tree = Outcomes(await Reader(catalog, delegations).TreeAsync(root.Job, Cancellation));

        Assert.Equal(
            [
                ("Write the notes", 1, "work", string.Empty, "Integrated", 0m, 0m),
                ("Update the todo", 1, "personal", "Simulator", "Running", 0.4m, 1m),
                ("Check the links", 2, "personal", string.Empty, "Running", 0m, 0m),
            ],
            tree.Children.Select(node => (
                node.Job.Instruction,
                node.Depth,
                node.Job.Connection.Match(name => name.Value, () => string.Empty),
                node.Spend.Session.Match(seen => seen.Provider.Name, () => string.Empty),
                node.Report.Match(report => report.Outcome.ToString(), () => node.Job.Status.ToString()),
                node.Spend.Spent.Sum(cost => cost.Amount),
                node.Spend.Carve.Match(carve => carve.Cost.Sum(cost => cost.Amount), () => 0m))));
        Assert.Equal([DelegationError.DepthExceeded], tree.Refused.Select(record => Avala.Testing.Outcomes.Present(record.Refusal)));
    }

    private DelegationReader Reader(TreeCatalog catalog, FakeDelegations delegations) =>
        new(catalog, delegations, new JobSpending(usage, budgets, new FakeSupervision(), sessions), Pages.Board());

    private static DelegationTree Outcomes(Option<DelegationTree> tree) => Avala.Testing.Outcomes.Present(tree);
}
