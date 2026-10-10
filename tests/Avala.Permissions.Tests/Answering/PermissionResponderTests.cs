using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Permissions.Answering;
using Avala.Permissions.Contracts;
using Avala.Permissions.Governance;
using Avala.Permissions.Links;
using Avala.Permissions.Policies;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workspaces.Contracts;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Permissions.Tests.Answering;

public sealed class PermissionResponderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider clock = new(Now);
    private readonly GovernanceBook book = new(new Governance.InMemoryGovernance());
    private readonly RecordingBus bus = new();
    private readonly AnsweringAgents agents = new();
    private readonly SessionId session = SessionId.New();
    private readonly TurnId turn = TurnId.New();
    private readonly SessionGovernor governor;

    public PermissionResponderTests() =>
        governor = new SessionGovernor(book, new SessionTerms(new NoPolicyFiles(), []), new PermissionResponder(agents, new SymbolicLinks(), clock), bus);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(PolicyAnswer.Allow, PermissionAnswer.Allow)]
    [InlineData(PolicyAnswer.Deny, PermissionAnswer.Deny)]
    public async Task ARuleThatAllowsOrDeniesAnswersTheAgentAndRecordsWhyAsync(PolicyAnswer answer, PermissionAnswer sent)
    {
        var rule = Govern(answer);

        await RequestAsync(ItemKind.Command, "dotnet ef database update");

        Assert.Equal([(session, new PermissionDecision(new ItemId("migrate"), sent))], agents.Responses);
        Assert.Equal(
            new PolicyDecision(session, turn, new ItemId("migrate"), Option<JobId>.None, ItemKind.Command, "dotnet ef database update", answer, rule, DecisionDelivery.Answered, Now),
            Decided());
    }

    [Fact]
    public async Task ARequestLeftToAHumanIsNotAnsweredButIsRecordedWithTheDefaultAsync()
    {
        await RequestAsync(ItemKind.Command, "dotnet ef database update");

        Assert.Empty(agents.Responses);
        var decision = Decided();
        Assert.Equal((PolicyAnswer.Ask, DecisionDelivery.LeftToHuman), (decision.Answer, decision.Delivery));
        Assert.True(decision.Rule.IsNone);
    }

    [Fact]
    public async Task AnAnswerTheAgentRejectsIsRecordedAsUndeliveredAsync()
    {
        Govern(PolicyAnswer.Allow);
        agents.Rejection = AgentError.NoPendingPermission;

        await RequestAsync(ItemKind.Command, "dotnet ef database update");

        Assert.Equal(DecisionDelivery.Undelivered, Decided().Delivery);
    }

    [Fact]
    public async Task AnEditIsDecidedOnItsPathInsideTheWorkingDirectoryAsync()
    {
        book.Keep(book.Of(session).OpenedIn("/worktrees/1", PermissionPolicy.BuiltIn, Report()));

        await RequestAsync(ItemKind.FileEdit, "/worktrees/1/src/app.cs");

        var decision = Decided();
        Assert.Equal(("src/app.cs", PolicyAnswer.Allow), (decision.Target, decision.Answer));
    }

    [Fact]
    public async Task ADecisionNamesTheJobOfItsSessionAsync()
    {
        var job = JobId.New();
        book.Keep(book.Of(session).WorkingOn(job));

        await RequestAsync(ItemKind.Command, "dotnet ef database update");

        Assert.Equal(job, Outcomes.Present(Decided().Job));
    }

    [Fact]
    public async Task ActivityOtherThanAPermissionRequestIsIgnoredAsync()
    {
        Govern(PolicyAnswer.Allow);

        await governor.HandleAsync(new AgentActivity(new ItemStarted(session, turn, new ItemId("migrate"), ItemKind.Command, "Run")), Cancellation);

        Assert.Empty(agents.Responses);
        Assert.Empty(bus.Published);
        Assert.Empty(book.OfSession(session));
    }

    [Fact]
    public async Task TheAuditListsTheDecisionsOfASessionAndOfEverySessionOfItsJobAsync()
    {
        var job = JobId.New();
        var recovered = SessionId.New();
        await RequestAsync(ItemKind.Command, "first");
        clock.Advance(TimeSpan.FromSeconds(1));
        await governor.HandleAsync(Requested(recovered, ItemKind.Command, "second"), Cancellation);

        book.Keep(book.Of(session).WorkingOn(job));
        book.Keep(book.Of(recovered).WorkingOn(job));

        Assert.Equal(["first"], book.OfSession(session).Select(decision => decision.Target));
        Assert.Equal(["first", "second"], book.OfJob(job).Select(decision => decision.Target));
        Assert.Empty(book.OfJob(JobId.New()));
    }

    private PolicyRule Govern(PolicyAnswer answer)
    {
        var rule = new PolicyRule(RuleOrigin.Repository, "migrations", ItemKind.Command, "dotnet ef *", RuleScope.Anywhere, answer);
        var policy = PermissionPolicy.With([rule]);
        book.Keep(book.Of(session).OpenedIn("/worktrees/1", policy, Report()));

        return rule;
    }

    private SessionPolicy Report() => new(session, PolicyFileStatus.Applied, Option<PolicyError>.None, [], CommittedFiles.Origin());

    private Task RequestAsync(ItemKind kind, string target) =>
        governor.HandleAsync(Requested(session, kind, target), Cancellation).AsTask();

    private AgentActivity Requested(SessionId requester, ItemKind kind, string target) =>
        new(new PermissionRequested(requester, turn, new ItemId("migrate"), "Run", kind, target));

    private PolicyDecision Decided() => Assert.IsType<PermissionDecided>(Assert.Single(bus.Published)).Decision;
}
