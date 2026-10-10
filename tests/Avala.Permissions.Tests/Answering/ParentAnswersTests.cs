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
using Microsoft.Extensions.Time.Testing;

namespace Avala.Permissions.Tests.Answering;

public sealed class ParentAnswersTests
{
    private const string Migration = "dotnet ef database update";

    private static readonly DateTimeOffset Now = new(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);

    private readonly GovernanceBook book = new(new Governance.InMemoryGovernance());
    private readonly RecordingBus bus = new();
    private readonly AnsweringAgents agents = new();
    private readonly SessionId child = SessionId.New();
    private readonly SessionId parent = SessionId.New();
    private readonly JobId childJob = JobId.New();
    private readonly JobId parentJob = JobId.New();
    private readonly SessionGovernor governor;
    private readonly ParentAnswers answers;
    private readonly HumanAnswers people;

    public ParentAnswersTests()
    {
        var clock = new FakeTimeProvider(Now);
        var ledger = new AnswerLedger(book, bus, clock);
        governor = new SessionGovernor(book, new SessionTerms(new NoPolicyFiles(), [new FixedTerms(new JobTerms(ReadOnly: false, parentJob))]), new PermissionResponder(agents, new SymbolicLinks(), clock), bus);
        answers = new ParentAnswers(book, agents, ledger);
        people = new HumanAnswers(book, agents, new RepositoryFiles(), ledger);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ARequestASupervisedChildWouldLeaveToAPersonIsLeftToItsParentInsteadAsync()
    {
        var asked = await AskedAsync();

        Assert.Equal((DecisionDelivery.LeftToParent, Option<JobId>.Some(parentJob), PolicyAnswer.Ask), (asked.Delivery, asked.Parent, asked.Answer));
        Assert.Empty(agents.Responses);
    }

    [Fact]
    public async Task AParentAllowsWhatItsOwnPolicyAllowsItselfAndTheAnswerIsAuditedAsTheParentsAsync()
    {
        await AskedAsync();
        await ParentAsync(Autonomy.Autonomous);

        var answer = Outcomes.Succeeds(await answers.AnswerAsync(child, Reply(PermissionAnswer.Allow), Cancellation));

        Assert.Equal((child, PermissionAnswer.Allow), (Assert.Single(agents.Responses).Item1, agents.Responses[0].Item2.Answer));
        Assert.Equal((Option<JobId>.Some(parentJob), Option<JobId>.Some(childJob), Migration), (answer.Parent, answer.Job, answer.Target));
        Assert.Equal([answer], book.AnswersOfJob(childJob));
        Assert.Equal([answer], book.AnswersGivenBy(parentJob));
        var decided = Assert.IsType<PermissionDecided>(bus.Published[^2]).Decision;
        Assert.Equal((DecisionDelivery.Answered, Option<JobId>.Some(parentJob)), (decided.Delivery, decided.Parent));
    }

    [Theory]
    [InlineData(Autonomy.Supervised)]
    [InlineData(null)]
    public async Task AParentCannotAllowWhatItsOwnPolicyWouldNotAllowItselfSoItGoesToAPersonAsync(Autonomy? level)
    {
        await AskedAsync();
        await ParentAsync(level ?? Autonomy.Autonomous, deniesMigrations: level is null);

        Assert.Equal(PolicyError.BeyondParent, Outcomes.FailsWith(await answers.AnswerAsync(child, Reply(PermissionAnswer.Allow), Cancellation)));

        Assert.Empty(agents.Responses);
        Assert.Empty(book.AnswersOfJob(childJob));
        var passed = Assert.IsType<PermissionDecided>(bus.Published[^1]).Decision;
        Assert.Equal((DecisionDelivery.LeftToHuman, Option<PassReason>.Some(PassReason.BeyondParent)), (passed.Delivery, passed.Passed));
    }

    [Fact]
    public async Task AParentMayDenyWithAMessageWhateverItsOwnPolicySaysAsync()
    {
        await AskedAsync();
        await ParentAsync(Autonomy.Supervised);

        var answer = Outcomes.Succeeds(await answers.AnswerAsync(child, Reply(PermissionAnswer.Deny) with { Message = "Use the staging database." }, Cancellation));

        Assert.Equal((PermissionAnswer.Deny, Option<string>.Some("Use the staging database.")), (agents.Responses[0].Item2.Answer, agents.Responses[0].Item2.Message));
        Assert.Equal(Option<JobId>.Some(parentJob), answer.Parent);
    }

    [Fact]
    public async Task ARequestPassedToAPersonIsNoLongerTheParentsToAnswerAndIsPassedOnlyOnceAsync()
    {
        await AskedAsync();
        await ParentAsync(Autonomy.Autonomous);

        var first = await answers.PassAsync(child, new ItemId("migrate"), PassReason.ParentTimedOut, Cancellation);
        var second = await answers.PassAsync(child, new ItemId("migrate"), PassReason.PassedByParent, Cancellation);

        Assert.Equal((true, false), (first, second));
        Assert.Equal(PolicyError.NotAwaitingAnswer, Outcomes.FailsWith(await answers.AnswerAsync(child, Reply(PermissionAnswer.Allow), Cancellation)));
        Assert.Empty(agents.Responses);
        Assert.Equal(Option<PassReason>.Some(PassReason.ParentTimedOut), Assert.Single(book.OfSession(child)).Passed);
        Outcomes.Succeeds(await people.AnswerAsync(child, new PermissionReply(new ItemId("migrate"), PermissionAnswer.Allow), Cancellation));
    }

    [Fact]
    public async Task APersonMayAnswerARequestLeftToTheParentAndThenTheParentCannotAsync()
    {
        await AskedAsync();
        await ParentAsync(Autonomy.Autonomous);

        Outcomes.Succeeds(await people.AnswerAsync(child, new PermissionReply(new ItemId("migrate"), PermissionAnswer.Deny), Cancellation));
        await governor.HandleAsync(new AgentActivity(new PermissionResolved(child, TurnId.New(), new ItemId("migrate"), PermissionAnswer.Deny)), Cancellation);

        Assert.Equal(PolicyError.NotAwaitingAnswer, Outcomes.FailsWith(await answers.AnswerAsync(child, Reply(PermissionAnswer.Allow), Cancellation)));
        Assert.False(await answers.PassAsync(child, new ItemId("migrate"), PassReason.ParentTimedOut, Cancellation));
        Assert.Single(agents.Responses);
    }

    [Fact]
    public async Task AJobThatIsNotTheChildsParentCannotAnswerItAsync()
    {
        await AskedAsync();
        var stranger = SessionId.New();
        book.Keep(book.Of(stranger).WorkingOn(JobId.New()) with { Policy = new PermissionPolicy([], Autonomy.Autonomous, FormStrategy.Recommended) });

        Assert.Equal(PolicyError.NotAwaitingAnswer, Outcomes.FailsWith(await answers.AnswerAsync(child, Reply(PermissionAnswer.Allow) with { Parent = stranger }, Cancellation)));
        Assert.Empty(agents.Responses);
    }

    [Fact]
    public async Task AParentAnswersAQuestionOfItsChildButCanOnlyDeclineAPermissionFormAsync()
    {
        await ParentAsync(Autonomy.Supervised);
        var question = await FormAsync("database", FormPurpose.Question);
        var permission = await FormAsync("grant", FormPurpose.Permission);
        var chosen = new FormAnswer(question.Item, [new FieldAnswer("choice") { Chosen = ["PostgreSQL"] }]);

        var answered = Outcomes.Succeeds(await answers.AnswerFormAsync(child, new ParentFormReply(parent, chosen), Cancellation));
        var refused = Outcomes.FailsWith(await answers.AnswerFormAsync(child, new ParentFormReply(parent, new FormAnswer(permission.Item, [new FieldAnswer("choice") { Chosen = ["PostgreSQL"] }])), Cancellation));

        Assert.Equal((DecisionDelivery.Answered, Option<FormAnswer>.Some(chosen), Option<JobId>.Some(parentJob)), (answered.Delivery, answered.Answer, answered.Parent));
        Assert.Equal(PolicyError.BeyondParent, refused);
        Assert.Equal([chosen], agents.Answers.Select(found => found.Item2));
        Assert.Equal(Option<PassReason>.Some(PassReason.BeyondParent), book.FormsOfSession(child).Single(form => form.Item == permission.Item).Passed);
    }

    [Fact]
    public async Task AnAnswerThatDoesNotFitTheFormIsRefusedAndTheFormStillWaitsForTheParentAsync()
    {
        await ParentAsync(Autonomy.Supervised);
        var question = await FormAsync("database", FormPurpose.Question);
        agents.Rejection = AgentError.InvalidAnswer;

        var refused = Outcomes.FailsWith(await answers.AnswerFormAsync(child, new ParentFormReply(parent, new FormAnswer(question.Item, [])), Cancellation));
        agents.Rejection = Option<AgentError>.None;

        Assert.Equal(PolicyError.InvalidAnswer, refused);
        Assert.Equal(DecisionDelivery.Answered, Outcomes.Succeeds(await answers.AnswerFormAsync(child, new ParentFormReply(parent, new FormAnswer(question.Item, [new FieldAnswer("choice") { Chosen = ["SQLite"] }])), Cancellation)).Delivery);
    }

    private ParentReply Reply(PermissionAnswer answer) => new(new ItemId("migrate"), parent, answer);

    private async Task<PolicyDecision> AskedAsync()
    {
        await governor.HandleAsync(new JobSessionStarted(childJob, child), Cancellation);
        await governor.HandleAsync(new AgentActivity(new PermissionRequested(child, TurnId.New(), new ItemId("migrate"), "Run", ItemKind.Command, Migration)), Cancellation);

        return Assert.IsType<PermissionDecided>(bus.Published[^1]).Decision;
    }

    private async Task<FormDecision> FormAsync(string item, FormPurpose purpose)
    {
        if (book.Of(child).Job.IsNone)
        {
            await governor.HandleAsync(new JobSessionStarted(childJob, child), Cancellation);
        }

        var form = new AgentForm(purpose, "Choose a database", "The service stores orders.", [new FormField("choice", "Database", "Which one?", FieldKind.SingleChoice, [new FormOption("PostgreSQL", "Relational."), new FormOption("SQLite", "One file.")])]);
        await governor.HandleAsync(new AgentActivity(new FormRequested(child, TurnId.New(), new ItemId(item), form)), Cancellation);

        return Assert.IsType<FormDecided>(bus.Published[^1]).Decision;
    }

    private Task ParentAsync(Autonomy level, bool deniesMigrations = false)
    {
        PolicyRule[] rules = deniesMigrations ? [new(RuleOrigin.Repository, "no migrations", ItemKind.Command, "dotnet ef *", RuleScope.Anywhere, PolicyAnswer.Deny)] : [];
        book.Keep(book.Of(parent) with { Policy = new PermissionPolicy(rules, level, FormStrategy.Recommended) });
        book.Keep(book.Of(parent).WorkingOn(parentJob));

        return Task.CompletedTask;
    }

    private sealed class FixedTerms(JobTerms terms) : IJobTerms
    {
        public ValueTask<JobTerms> OfAsync(JobId job, CancellationToken cancellationToken) => ValueTask.FromResult(terms);
    }
}
