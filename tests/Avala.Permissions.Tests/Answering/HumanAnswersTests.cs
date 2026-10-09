using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Permissions.Answering;
using Avala.Permissions.Contracts;
using Avala.Permissions.Governance;
using Avala.Permissions.Policies;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workspaces.Contracts;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Permissions.Tests.Answering;

public sealed class HumanAnswersTests
{
    private const string Migration = "dotnet ef database update";

    private static readonly DateTimeOffset Now = new(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);

    private readonly GovernanceBook book = new();
    private readonly RecordingBus bus = new();
    private readonly AnsweringAgents agents = new();
    private readonly SessionId session = SessionId.New();
    private readonly JobId job = JobId.New();
    private readonly SessionGovernor governor;
    private readonly HumanAnswers answers;

    public HumanAnswersTests()
    {
        var clock = new FakeTimeProvider(Now);
        governor = new SessionGovernor(book, new NoPolicyFiles(), new PermissionResponder(agents, clock), bus);
        answers = new HumanAnswers(book, agents, bus, clock);
        book.Keep(book.Of(session).WorkingOn(job));
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ADenialWithAMessageReachesTheAgentAndIsAuditedAsync()
    {
        await RequestAsync("migrate");

        var answer = Outcomes.Succeeds(await answers.AnswerAsync(session, Deny("migrate", "Use the staging database instead."), Cancellation));

        var (answeredIn, decision) = Assert.Single(agents.Responses);
        Assert.Equal((session, PermissionAnswer.Deny, Option<string>.Some("Use the staging database instead.")), (answeredIn, decision.Answer, decision.Message));
        Assert.Equal(
            new HumanAnswer(session, job, new ItemId("migrate"), ItemKind.Command, Migration, PermissionAnswer.Deny, "Use the staging database instead.", Option<PolicyRule>.None, Now),
            answer);
        Assert.Equal(new PermissionAnswered(answer), bus.Published[^1]);
        Assert.Equal([answer], book.AnswersOfJob(job));
        Assert.Empty(book.SessionRulesOf(session));
    }

    [Fact]
    public async Task DontAskAgainBecomesASessionRuleBeforeTheAgentGoesOnAndAnswersTheNextIdenticalRequestAsync()
    {
        await RequestAsync("migrate");
        var rulesWhenAnswered = new List<PolicyRule>();
        agents.OnRespond = () => rulesWhenAnswered.AddRange(book.SessionRulesOf(session));

        var answer = Outcomes.Succeeds(await answers.AnswerAsync(session, new PermissionReply(new ItemId("migrate"), PermissionAnswer.Allow) { DontAskAgain = true }, Cancellation));
        agents.OnRespond = () => { };
        await RequestAsync("migrate-again");

        var rule = new PolicyRule(RuleOrigin.Session, HumanAnswers.DontAskAgain, ItemKind.Command, Migration, RuleScope.Anywhere, PolicyAnswer.Allow);
        Assert.Equal([rule], rulesWhenAnswered);
        Assert.Equal(Option<PolicyRule>.Some(rule), answer.SessionRule);
        Assert.Equal([rule], book.SessionRulesOf(session));
        var next = Assert.IsType<PermissionDecided>(bus.Published[^1]).Decision;
        Assert.Equal((PolicyAnswer.Allow, DecisionDelivery.Answered, Option<PolicyRule>.Some(rule)), (next.Answer, next.Delivery, next.Rule));
    }

    [Fact]
    public async Task OnlyARequestLeftToAHumanCanBeAnsweredAsync()
    {
        book.Keep(book.Of(session).OpenedIn("/worktrees/1", PermissionPolicy.With([new(RuleOrigin.Repository, "ef", ItemKind.Command, "dotnet ef *", RuleScope.Anywhere, PolicyAnswer.Allow)]), Report()));
        await RequestAsync("migrate");
        var answered = agents.Responses.Count;

        Assert.Equal(PolicyError.NotAwaitingAnswer, Outcomes.FailsWith(await answers.AnswerAsync(session, Deny("migrate", "No."), Cancellation)));
        Assert.Equal(PolicyError.NotAwaitingAnswer, Outcomes.FailsWith(await answers.AnswerAsync(session, Deny("unknown", "No."), Cancellation)));
        Assert.Equal(answered, agents.Responses.Count);
        Assert.Empty(book.AnswersOfJob(job));
    }

    [Fact]
    public async Task AnAnswerTheAgentNoLongerAwaitsLeavesNoSessionRuleBehindAsync()
    {
        await RequestAsync("migrate");
        agents.Rejection = AgentError.NoPendingPermission;

        var rejected = await answers.AnswerAsync(session, new PermissionReply(new ItemId("migrate"), PermissionAnswer.Allow) { DontAskAgain = true }, Cancellation);

        Assert.Equal(PolicyError.NotAwaitingAnswer, Outcomes.FailsWith(rejected));
        Assert.Empty(book.SessionRulesOf(session));
        Assert.Empty(book.AnswersOfJob(job));
    }

    private static PermissionReply Deny(string item, string message) => new(new ItemId(item), PermissionAnswer.Deny) { Message = message };

    private SessionPolicy Report() => new(session, PolicyFileStatus.Applied, Option<PolicyError>.None, [], CommittedFiles.Origin());

    private Task RequestAsync(string item) =>
        governor.HandleAsync(new AgentActivity(new PermissionRequested(session, TurnId.New(), new ItemId(item), "Run", ItemKind.Command, Migration)), Cancellation).AsTask();
}
