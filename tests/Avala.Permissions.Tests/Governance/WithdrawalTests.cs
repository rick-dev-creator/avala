using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Permissions.Answering;
using Avala.Permissions.Contracts;
using Avala.Permissions.Governance;
using Avala.Permissions.Links;
using Avala.Sdk;
using Avala.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Permissions.Tests.Governance;

public sealed class WithdrawalTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);

    private static readonly AgentForm Question = new(
        FormPurpose.Question,
        "Choose a database",
        "The service needs storage.",
        [new FormField("database", "Database", "Which database?", FieldKind.SingleChoice, [new FormOption("SQLite", "A file.", Recommended: true)])]);

    private readonly InMemoryGovernance store = new();
    private readonly GovernanceBook book;
    private readonly RecordingBus bus = new();
    private readonly AnsweringAgents agents = new();
    private readonly SessionId session = SessionId.New();
    private readonly TurnId turn = TurnId.New();
    private readonly JobId job = JobId.New();
    private readonly SessionGovernor governor;
    private readonly HumanAnswers answers;

    public WithdrawalTests()
    {
        var clock = new FakeTimeProvider(Now);
        book = new GovernanceBook(store);
        governor = new SessionGovernor(book, new NoPolicyFiles(), new PermissionResponder(agents, new SymbolicLinks(), clock), bus);
        answers = new HumanAnswers(book, agents, bus, clock);
        book.Keep(book.Of(session).WorkingOn(job));
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task APermissionTheHarnessWithdrawsIsAuditedOnceAsWithdrawnAndCanNoLongerBeAnsweredAsync()
    {
        await ActAsync(new PermissionRequested(session, turn, new ItemId("migrate"), "Run", ItemKind.Command, "dotnet ef database update"));

        await ActAsync(new RequestWithdrawn(session, turn, new ItemId("migrate")));
        var refused = await answers.AnswerAsync(session, new PermissionReply(new ItemId("migrate"), PermissionAnswer.Allow), Cancellation);

        var audited = Assert.Single(book.OfJob(job));
        Assert.Equal(DecisionDelivery.Withdrawn, audited.Delivery);
        Assert.Equal(new PermissionDecided(audited), bus.Published[^1]);
        Assert.Equal(PolicyError.NotAwaitingAnswer, Outcomes.FailsWith(refused));
        Assert.Empty(agents.Responses);
        Assert.Equal(audited, store.Recorded[^1]);
    }

    [Fact]
    public async Task AFormTheHarnessWithdrawsIsAuditedOnceAsWithdrawnAsync()
    {
        await ActAsync(new FormRequested(session, turn, new ItemId("question"), Question));

        await ActAsync(new RequestWithdrawn(session, turn, new ItemId("question")));

        var audited = Assert.Single(book.FormsOfJob(job));
        Assert.Equal((DecisionDelivery.Withdrawn, true), (audited.Delivery, audited.Answer.IsNone));
        Assert.Equal(new FormDecided(audited), bus.Published[^1]);
    }

    [Fact]
    public async Task AWithdrawalOfEarlierRunsIsRestoredAsTheOneDecisionOfItsRequestAsync()
    {
        await ActAsync(new PermissionRequested(session, turn, new ItemId("migrate"), "Run", ItemKind.Command, "dotnet ef database update"));
        await ActAsync(new RequestWithdrawn(session, turn, new ItemId("migrate")));
        var restarted = new InMemoryGovernance
        {
            Earlier = GovernanceHistory.Empty with { Decisions = [.. store.Recorded.OfType<PolicyDecision>()] },
        };
        var restored = new GovernanceBook(restarted);

        await restored.RunAsync(Cancellation);

        Assert.Equal(DecisionDelivery.Withdrawn, Assert.Single(restored.OfJob(job)).Delivery);
    }

    private Task ActAsync(IAgentEvent agentEvent) => governor.HandleAsync(new AgentActivity(agentEvent), Cancellation).AsTask();
}
