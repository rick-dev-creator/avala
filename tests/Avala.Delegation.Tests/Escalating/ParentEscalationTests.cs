using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Delegation.Contracts;
using Avala.Delegation.Escalating;
using Avala.Delegation.Records;
using Avala.Delegation.Tests.Delegating;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Delegation.Tests.Escalating;

public sealed class ParentEscalationTests
{
    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero));
    private readonly RecordingBus bus = new();
    private readonly ReturningAgents agents = new();
    private readonly FakeJobs jobs = new();
    private readonly FixedAudit audit = new();
    private readonly RecordingParentAnswers answers = new();
    private readonly DelegationBook book = new(new Records.InMemoryDelegations());
    private readonly ParentNotes notes;
    private readonly ChildAnswers tool;
    private readonly JobId parent;
    private readonly JobId child;
    private readonly SessionId childSession = SessionId.New();
    private readonly SessionId parentSession = SessionId.New();

    public ParentEscalationTests()
    {
        notes = new ParentNotes(new DelegationJournal(book, bus, agents, clock), new ParentWaking(jobs, jobs, jobs));
        tool = new ChildAnswers(answers, audit, agents);
        parent = jobs.Running();
        child = jobs.Running(parent);
        audit.Effective[parentSession] = Autonomy.Supervised;
        audit.Working[parentSession] = parent;
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ARunningParentIsToldWhatItsChildAsksAndHowToAnswerAndNothingEverGoesToAPersonAsync()
    {
        await DelegatedAsync();
        jobs.Steerable = true;

        await notes.HandleAsync(new FormDecided(Asked()), Cancellation);
        clock.Advance(TimeSpan.FromDays(1));

        var (told, note) = Assert.Single(jobs.Steered);
        var asked = Assert.Single(bus.Published.OfType<ParentAsked>());
        Assert.Equal((parent, new ItemId("database"), false), (told, asked.Item, asked.Woken));
        Assert.StartsWith("Sub-agent \"Choose the orders database\" is waiting for you: it asks: Choose a database", note, StringComparison.Ordinal);
        Assert.Contains("never to a person", note, StringComparison.Ordinal);
        Assert.EndsWith($$"""answer_child {"child":"{{child.Value}}","request":"database","decision":"allow"}""", note, StringComparison.Ordinal);
        Assert.Equal([MessageAuthor.Avala], jobs.SteeredBy);
    }

    [Theory]
    [InlineData(JobStatus.AwaitingReview)]
    [InlineData(JobStatus.NeedsHelp)]
    public async Task AnIdleParentIsWokenWithItsChildsQuestionAsAvalasOwnMessageAsync(JobStatus status)
    {
        await DelegatedAsync();
        jobs.Is(parent, status);

        await notes.HandleAsync(new FormDecided(Asked()), Cancellation);

        var woken = status == JobStatus.AwaitingReview
            ? jobs.SentBack.Select(sent => (sent.Job, sent.Message.Text, sent.Message.CausedBy)).ToList()
            : jobs.Continued.Zip(jobs.ContinuedBy, (continued, by) => (continued.Job, continued.Message, by)).ToList();
        var (job, text, causedBy) = Assert.Single(woken);
        Assert.Equal((parent, MessageAuthor.Avala), (job, causedBy));
        Assert.StartsWith("Sub-agent \"Choose the orders database\" is waiting for you", text, StringComparison.Ordinal);
        Assert.True(Assert.Single(bus.Published.OfType<ParentAsked>()).Woken, "The idle parent was not said to be woken by its child's question");
    }

    [Theory]
    [InlineData(HoldReason.BudgetExceeded)]
    [InlineData(HoldReason.Stopped)]
    public async Task AParentHeldOverItsBudgetOrStoppedByThePersonIsNotContinuedByItsChildsQuestionAsync(HoldReason reason)
    {
        await DelegatedAsync();
        jobs.HeldFor(parent, reason);

        await notes.HandleAsync(new FormDecided(Asked()), Cancellation);

        Assert.Empty(jobs.Continued);
    }

    [Fact]
    public async Task AParentThatCannotBeToldYetIsWokenWithTheQuestionOnceItStopsWorkingAsync()
    {
        await DelegatedAsync();

        await notes.HandleAsync(new FormDecided(Asked()), Cancellation);
        Assert.Empty(bus.Published.OfType<ParentAsked>());
        jobs.Is(parent, JobStatus.AwaitingReview);
        await notes.HandleAsync(new JobProgressed(parent, JobStatus.AwaitingReview), Cancellation);
        await notes.HandleAsync(new JobProgressed(parent, JobStatus.AwaitingReview), Cancellation);

        var (job, message) = Assert.Single(jobs.SentBack);
        Assert.Equal((parent, MessageAuthor.Avala), (job, message.CausedBy));
        Assert.True(Assert.Single(bus.Published.OfType<ParentAsked>()).Woken, "The parent was not woken once it stopped working");
    }

    [Fact]
    public async Task AQuestionAnsweredBeforeItsParentCouldBeToldDoesNotWakeTheParentAsync()
    {
        await DelegatedAsync();

        await notes.HandleAsync(new FormDecided(Asked()), Cancellation);
        await notes.HandleAsync(new FormDecided(Asked() with { Delivery = DecisionDelivery.Withdrawn }), Cancellation);
        await notes.HandleAsync(new JobProgressed(parent, JobStatus.AwaitingReview), Cancellation);

        Assert.Empty(jobs.SentBack);
    }

    [Theory]
    [InlineData(JobStatus.Approved)]
    [InlineData(JobStatus.Discarded)]
    [InlineData(JobStatus.Failed)]
    public async Task AChildWhoseParentHasEndedIsDiscardedInsteadOfAskingAPersonAsync(JobStatus status)
    {
        await DelegatedAsync();
        jobs.Is(parent, status);

        await notes.HandleAsync(new FormDecided(Asked()), Cancellation);

        Assert.Equal([child], jobs.Discarded);
        Assert.Empty(bus.Published.OfType<ParentAsked>());
    }

    [Fact]
    public async Task AChildIsGovernedByTheTermsItWasDelegatedUnderEvenBeforeItsDelegationIsRecordedAsync()
    {
        var terms = new ChildTerms(book, jobs);
        var unrecorded = jobs.Running(parent);
        terms.Submitting(parent, Record(unrecorded) with { Child = Option<JobId>.None, Role = ChildRole.Reviewer });
        var early = await terms.OfAsync(unrecorded, Cancellation);
        terms.Submitted(parent);
        await DelegatedAsync();

        Assert.Equal(new JobTerms(ReadOnly: true, parent), early);
        Assert.Equal(new JobTerms(ReadOnly: false, parent), await terms.OfAsync(child, Cancellation));
        Assert.Equal(JobTerms.Full, await terms.OfAsync(unrecorded, Cancellation));
        Assert.Equal(JobTerms.Full, await terms.OfAsync(parent, Cancellation));
    }

    [Fact]
    public async Task TheParentAnswersItsChildsQuestionThroughTheToolAsync()
    {
        audit.Forms.Add(Asked());
        answers.Answered = Asked() with { Delivery = DecisionDelivery.Answered };

        await CallAsync("allow", """, "fields": [ { "id": "database", "chosen": ["PostgreSQL"] } ], "message": "Keep it simple." """);

        var (asking, reply) = Assert.Single(answers.FormReplies);
        Assert.Equal((childSession, new ItemId("database"), parentSession, Option<string>.Some("Keep it simple.")), (asking, reply.Answer.Item, reply.Parent, reply.Answer.Message));
        Assert.Equal(["PostgreSQL"], Assert.Single(reply.Answer.Fields).Chosen);
        Assert.Equal("""{"answered":"allow"}""", Assert.Single(agents.Results).Result.Content);
    }

    [Theory]
    [InlineData("waiting", "notWaiting")]
    [InlineData("stranger", "notYourChild")]
    [InlineData("invalid", "invalidAnswer")]
    [InlineData("malformed", "malformedInput")]
    [InlineData("person", "malformedInput")]
    public async Task TheToolRefusesWhatTheParentMayNotAnswerAndAnswersNothingAsync(string situation, string refused)
    {
        audit.Forms.Add(situation == "waiting" ? Asked() with { Delivery = DecisionDelivery.LeftToHuman } : Asked());
        audit.Working[parentSession] = situation == "stranger" ? JobId.New() : parent;
        answers.Refusal = situation == "invalid" ? PolicyError.InvalidAnswer : Option<PolicyError>.None;

        await CallAsync(situation switch { "malformed" => "maybe", "person" => "person", _ => "allow" });

        var result = Assert.Single(agents.Results).Result;
        Assert.True(result.IsError);
        Assert.Contains($"\"refused\":\"{refused}\"", result.Content, StringComparison.Ordinal);
        Assert.Equal(situation == "invalid" ? 1 : 0, answers.FormReplies.Count);
    }

    private Task CallAsync(string decision, string extra = "") =>
        tool.HandleAsync(
            new AgentActivity(new ToolCalled(parentSession, TurnId.New(), new ItemId("answer"), AnswerChildTool.Name, $$"""{ "child": "{{child.Value}}", "request": "database", "decision": "{{decision}}"{{extra}} }""")),
            Cancellation).AsTask();

    private FormDecision Asked() =>
        new(
            childSession,
            TurnId.New(),
            new ItemId("database"),
            child,
            new AgentForm(
                FormPurpose.Question,
                "Choose a database",
                "The service needs to store its orders.",
                [new FormField("database", "Database", "Which database should the service use?", FieldKind.SingleChoice, [new FormOption("PostgreSQL", "Relational."), new FormOption("SQLite", "A single file.")])]),
            Autonomy.Supervised,
            Option<FormAnswer>.None,
            [],
            DecisionDelivery.LeftToParent,
            clock.GetUtcNow())
        {
            Parent = parent,
        };

    private DelegationRecord Record(JobId job) =>
        new(parentSession, new ItemId("delegate-database"), "Choose the orders database\nfor the service", clock.GetUtcNow())
        {
            Parent = parent,
            Child = job,
        };

    private Task DelegatedAsync() => book.KeepAsync(Record(child), Cancellation);
}
