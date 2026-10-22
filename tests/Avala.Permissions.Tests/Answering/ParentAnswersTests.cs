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
        var ledger = new AnswerLedger(book, bus, clock, new RecordedRounds());
        governor = new SessionGovernor(book, new SessionTerms(new NoPolicyFiles(), [new FixedTerms(new JobTerms(ReadOnly: false, parentJob))]), new PermissionResponder(agents, new RequestPlaces(new SymbolicLinks(), new FixedReadableFolders([])), clock, []), bus);
        answers = new ParentAnswers(book, agents, ledger);
        people = new HumanAnswers(book, agents, new RepositoryFiles(), ledger);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AChildsPermissionRequestGoesToAPersonAndNeverToItsParentAsync()
    {
        var asked = await AskedAsync();

        Assert.Equal((DecisionDelivery.LeftToHuman, Option<JobId>.None, PolicyAnswer.Ask), (asked.Delivery, asked.Parent, asked.Answer));
        Outcomes.Succeeds(await people.AnswerAsync(child, new PermissionReply(new ItemId("migrate"), PermissionAnswer.Allow), Cancellation));
        Assert.Equal((child, PermissionAnswer.Allow), (Assert.Single(agents.Responses).Item1, agents.Responses[0].Item2.Answer));
    }

    [Fact]
    public async Task AChildsQuestionGoesToItsParentAndAPermissionAskedThroughAFormGoesToAPersonAsync()
    {
        var question = await FormAsync("database", FormPurpose.Question);
        var permission = await FormAsync("grant", FormPurpose.Permission);

        Assert.Equal((DecisionDelivery.LeftToParent, Option<JobId>.Some(parentJob)), (question.Delivery, question.Parent));
        Assert.Equal((DecisionDelivery.LeftToHuman, Option<JobId>.None), (permission.Delivery, permission.Parent));
    }

    [Fact]
    public async Task AChildsQuestionGoesToItsParentEvenWhereItsAutonomyWouldHaveAnsweredItAsync()
    {
        await governor.HandleAsync(new JobSessionStarted(childJob, child), Cancellation);
        book.Keep(book.Of(child) with { Policy = new PermissionPolicy([], Autonomy.Autonomous, FormStrategy.Recommended) });

        var question = await FormAsync("database", FormPurpose.Question);

        Assert.Equal(DecisionDelivery.LeftToParent, question.Delivery);
        Assert.Empty(agents.Answers);
    }

    [Fact]
    public async Task AParentAnswersAQuestionOfItsChildOnceAsync()
    {
        await ParentAsync(Autonomy.Supervised);
        var question = await FormAsync("database", FormPurpose.Question);
        var chosen = new FormAnswer(question.Item, [new FieldAnswer("choice") { Chosen = ["PostgreSQL"] }]) { AnsweredBy = MessageAuthor.Avala };

        var answered = Outcomes.Succeeds(await answers.AnswerFormAsync(child, new ParentFormReply(parent, chosen with { AnsweredBy = MessageAuthor.Person }), Cancellation));
        var again = Outcomes.FailsWith(await answers.AnswerFormAsync(child, new ParentFormReply(parent, chosen), Cancellation));

        Assert.Equal((DecisionDelivery.Answered, Option<FormAnswer>.Some(chosen), Option<JobId>.Some(parentJob)), (answered.Delivery, answered.Answer, answered.Parent));
        Assert.Equal(PolicyError.NotAwaitingAnswer, again);
        Assert.Equal([chosen], agents.Answers.Select(found => found.Item2));
    }

    [Fact]
    public async Task AJobThatIsNotTheChildsParentCannotAnswerItsQuestionAsync()
    {
        var question = await FormAsync("database", FormPurpose.Question);
        var stranger = SessionId.New();
        book.Keep(book.Of(stranger).WorkingOn(JobId.New()));

        Assert.Equal(PolicyError.NotAwaitingAnswer, Outcomes.FailsWith(await answers.AnswerFormAsync(child, new ParentFormReply(stranger, new FormAnswer(question.Item, [new FieldAnswer("choice") { Chosen = ["SQLite"] }])), Cancellation)));
        Assert.Empty(agents.Answers);
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
