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

public sealed class HumanAnswersTests
{
    private const string Migration = "dotnet ef database update";

    private static readonly DateTimeOffset Now = new(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);

    private readonly GovernanceBook book = new(new Governance.InMemoryGovernance());
    private readonly RecordingBus bus = new();
    private readonly AnsweringAgents agents = new();
    private readonly RepositoryFiles repository = new();
    private readonly SessionId session = SessionId.New();
    private readonly JobId job = JobId.New();
    private readonly SessionGovernor governor;
    private readonly HumanAnswers answers;

    public HumanAnswersTests()
    {
        var clock = new FakeTimeProvider(Now);
        governor = new SessionGovernor(book, new NoPolicyFiles(), new PermissionResponder(agents, new SymbolicLinks(), clock), bus);
        answers = new HumanAnswers(book, agents, repository, new AnswerLedger(book, bus, clock));
        book.Keep(book.Of(session).WorkingOn(job));
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static PolicyRule JobRule => new(RuleOrigin.Job, "don't ask again for this job", ItemKind.Command, Migration, RuleScope.Anywhere, PolicyAnswer.Allow);

    [Fact]
    public async Task ADenialWithAMessageReachesTheAgentAndIsAuditedAsync()
    {
        await RequestAsync(session, "migrate");

        var answer = Outcomes.Succeeds(await answers.AnswerAsync(session, Deny("migrate", "Use the staging database instead."), Cancellation));

        var (answeredIn, decision) = Assert.Single(agents.Responses);
        Assert.Equal((session, PermissionAnswer.Deny, Option<string>.Some("Use the staging database instead.")), (answeredIn, decision.Answer, decision.Message));
        Assert.Equal(
            new HumanAnswer(session, job, new ItemId("migrate"), ItemKind.Command, Migration, PermissionAnswer.Deny, "Use the staging database instead.", Option<PolicyRule>.None, Now),
            answer);
        Assert.Equal(new PermissionAnswered(answer), bus.Published[^1]);
        Assert.Equal([answer], book.AnswersOfJob(job));
        Assert.Empty(book.JobRulesOf(job));
        Assert.Empty(repository.Added);
    }

    [Fact]
    public async Task DontAskAgainForThisJobBecomesAJobRuleBeforeTheAgentGoesOnAndAnswersTheSameRequestInALaterSessionOfTheJobAsync()
    {
        await RequestAsync(session, "migrate");
        var rulesWhenAnswered = new List<PolicyRule>();
        agents.OnRespond = () => rulesWhenAnswered.AddRange(book.JobRulesOf(job));

        var answer = Outcomes.Succeeds(await answers.AnswerAsync(session, Allow("migrate", Remember.ForThisJob), Cancellation));
        agents.OnRespond = () => { };
        var later = SessionId.New();
        book.Keep(book.Of(later).WorkingOn(job));
        await RequestAsync(later, "migrate-again");

        Assert.Equal([JobRule], rulesWhenAnswered);
        Assert.Equal(Option<PolicyRule>.Some(JobRule), answer.Rule);
        Assert.Equal([JobRule], book.JobRulesOf(job));
        var next = Assert.IsType<PermissionDecided>(bus.Published[^1]).Decision;
        Assert.Equal((later, PolicyAnswer.Allow, DecisionDelivery.Answered, Option<PolicyRule>.Some(JobRule)), (next.Session, next.Answer, next.Delivery, next.Rule));
        Assert.Empty(repository.Added);
    }

    [Fact]
    public async Task AJobRuleNeverAnswersAnotherJobAsync()
    {
        await RequestAsync(session, "migrate");
        Outcomes.Succeeds(await answers.AnswerAsync(session, Allow("migrate", Remember.ForThisJob), Cancellation));
        var other = SessionId.New();
        book.Keep(book.Of(other).WorkingOn(JobId.New()));

        await RequestAsync(other, "migrate");

        var asked = Assert.IsType<PermissionDecided>(bus.Published[^1]).Decision;
        Assert.Equal((other, PolicyAnswer.Ask, DecisionDelivery.LeftToHuman), (asked.Session, asked.Answer, asked.Delivery));
    }

    [Fact]
    public async Task OnlyARequestLeftToAHumanCanBeAnsweredAsync()
    {
        book.Keep(book.Of(session).OpenedIn("/worktrees/1", PermissionPolicy.With([new(RuleOrigin.Repository, "ef", ItemKind.Command, "dotnet ef *", RuleScope.Anywhere, PolicyAnswer.Allow)]), Report()));
        await RequestAsync(session, "migrate");
        var answered = agents.Responses.Count;

        Assert.Equal(PolicyError.NotAwaitingAnswer, Outcomes.FailsWith(await answers.AnswerAsync(session, Deny("migrate", "No."), Cancellation)));
        Assert.Equal(PolicyError.NotAwaitingAnswer, Outcomes.FailsWith(await answers.AnswerAsync(session, Deny("unknown", "No."), Cancellation)));
        Assert.Equal(answered, agents.Responses.Count);
        Assert.Empty(book.AnswersOfJob(job));
    }

    [Fact]
    public async Task AnAnswerTheAgentNoLongerAwaitsLeavesNoRuleBehindAsync()
    {
        await RequestAsync(session, "migrate");
        agents.Rejection = AgentError.NoPendingPermission;

        var rejected = await answers.AnswerAsync(session, Allow("migrate", Remember.InThisRepository), Cancellation);

        Assert.Equal(PolicyError.NotAwaitingAnswer, Outcomes.FailsWith(rejected));
        Assert.Empty(book.JobRulesOf(job));
        Assert.Empty(book.AnswersOfJob(job));
        Assert.Empty(repository.Added);
    }

    [Fact]
    public async Task AlwaysInThisRepositoryAddsTheExactRuleTheDecisionOfferedWithTheAnswerGivenAndKeepsAJobRuleAsync()
    {
        await RequestAsync(session, "migrate");
        var offered = Outcomes.Present(Assert.IsType<PermissionDecided>(bus.Published[^1]).Decision.RepositoryRule);

        var answer = Outcomes.Succeeds(await answers.AnswerAsync(session, new PermissionReply(new ItemId("migrate"), PermissionAnswer.Deny) { Remember = Remember.InThisRepository }, Cancellation));

        var denied = new PolicyRule(RuleOrigin.Repository, "always in this repository", ItemKind.Command, Migration, RuleScope.Anywhere, PolicyAnswer.Deny);
        Assert.Equal(offered with { Answer = PolicyAnswer.Deny }, denied);
        Assert.Equal([(job, denied)], repository.Added);
        Assert.Equal((Option<PolicyRule>.Some(denied), Option<PolicyError>.None), (answer.RepositoryRule, answer.RepositoryError));
        Assert.Equal([JobRule with { Answer = PolicyAnswer.Deny }], book.JobRulesOf(job));
        Assert.Equal(PermissionAnswer.Deny, Assert.Single(agents.Responses).Item2.Answer);
    }

    [Fact]
    public async Task ARepositoryRuleThatCannotBeWrittenIsReportedWhileTheAnswerAndTheJobRuleStillApplyAsync()
    {
        repository.Refusal = PolicyError.Malformed;
        await RequestAsync(session, "migrate");

        var answer = Outcomes.Succeeds(await answers.AnswerAsync(session, Allow("migrate", Remember.InThisRepository), Cancellation));

        Assert.Equal((Option<PolicyRule>.None, Option<PolicyError>.Some(PolicyError.Malformed)), (answer.RepositoryRule, answer.RepositoryError));
        Assert.Equal(Option<PolicyRule>.Some(JobRule), answer.Rule);
        Assert.Equal(PermissionAnswer.Allow, Assert.Single(agents.Responses).Item2.Answer);
    }

    private static PermissionReply Allow(string item, Remember remember) => new(new ItemId(item), PermissionAnswer.Allow) { Remember = remember };

    private static PermissionReply Deny(string item, string message) => new(new ItemId(item), PermissionAnswer.Deny) { Message = message };

    private SessionPolicy Report() => new(session, PolicyFileStatus.Applied, Option<PolicyError>.None, [], CommittedFiles.Origin());

    private Task RequestAsync(SessionId asking, string item) =>
        governor.HandleAsync(new AgentActivity(new PermissionRequested(asking, TurnId.New(), new ItemId(item), "Run", ItemKind.Command, Migration)), Cancellation).AsTask();
}
