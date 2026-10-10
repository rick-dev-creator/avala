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

public sealed class ParentEscalationTests : IAsyncDisposable
{
    private const string Migration = "dotnet ef database update";

    private static readonly TimeSpan Window = TimeSpan.FromSeconds(120);

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
        notes = new ParentNotes(new DelegationJournal(book, bus, agents, clock), jobs, answers, clock);
        tool = new ChildAnswers(answers, audit, agents);
        parent = jobs.Running();
        child = jobs.Running(parent);
        audit.Effective[parentSession] = Autonomy.Supervised;
        audit.Working[parentSession] = parent;
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public ValueTask DisposeAsync() => notes.DisposeAsync();

    [Fact]
    public async Task TheParentIsToldWhatItsChildAsksAndHowToAnswerAndTheRequestIsPassedToAPersonWhenItsWindowPassesAsync()
    {
        await DelegatedAsync();
        jobs.Steerable = true;

        await notes.HandleAsync(new PermissionDecided(Asked()), Cancellation);
        var asked = Assert.Single(bus.Published.OfType<ParentAsked>());
        clock.Advance(Window);
        var passed = await answers.FirstPass.WaitAsync(Cancellation);

        var (told, note) = Assert.Single(jobs.Steered);
        Assert.Equal(parent, told);
        Assert.StartsWith($"Sub-agent \"Migrate the database\" is waiting for you: it wants to run a command: {Migration}", note, StringComparison.Ordinal);
        Assert.EndsWith($$"""answer_child {"child":"{{child.Value}}","request":"migrate","decision":"allow"}""", note, StringComparison.Ordinal);
        Assert.Equal((clock.GetUtcNow(), new ItemId("migrate")), (asked.Until, asked.Item));
        Assert.Equal(PassReason.ParentTimedOut, passed);
    }

    [Fact]
    public async Task ARequestAnsweredWithinItsWindowIsNeverPassedAsync()
    {
        await DelegatedAsync();
        jobs.Steerable = true;

        await notes.HandleAsync(new PermissionDecided(Asked()), Cancellation);
        await notes.HandleAsync(new PermissionDecided(Asked() with { Delivery = DecisionDelivery.Answered }), Cancellation);
        clock.Advance(Window * 2);

        Assert.False(answers.FirstPass.IsCompleted, "A request answered by the parent was passed to a person when its window passed");
    }

    [Fact]
    public async Task AParentThatCannotBeToldLeavesTheRequestToAPersonAtOnceAsync()
    {
        await DelegatedAsync();

        await notes.HandleAsync(new PermissionDecided(Asked()), Cancellation);

        Assert.Equal([(childSession, new ItemId("migrate"), PassReason.ParentUnreachable)], answers.Passed);
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
    public async Task TheParentAnswersItsChildsRequestThroughTheToolAsync()
    {
        audit.Decisions.Add(Asked());

        await CallAsync("allow", """, "message": "Go ahead." """);

        var (asking, reply) = Assert.Single(answers.Replies);
        Assert.Equal((childSession, new ItemId("migrate"), parentSession, PermissionAnswer.Allow, Option<string>.Some("Go ahead.")), (asking, reply.Item, reply.Parent, reply.Answer, reply.Message));
        Assert.Equal("""{"answered":"allow"}""", Assert.Single(agents.Results).Result.Content);
    }

    [Fact]
    public async Task TheParentPassesItsChildsRequestToAPersonThroughTheToolAsync()
    {
        audit.Decisions.Add(Asked());

        await CallAsync("person");

        Assert.Equal([(childSession, new ItemId("migrate"), PassReason.PassedByParent)], answers.Passed);
        Assert.False(Assert.Single(agents.Results).Result.IsError);
    }

    [Theory]
    [InlineData("waiting", "notWaiting")]
    [InlineData("stranger", "notYourChild")]
    [InlineData("beyond", "beyondYourRules")]
    [InlineData("malformed", "malformedInput")]
    public async Task TheToolRefusesWhatTheParentMayNotAnswerAndAnswersNothingAsync(string situation, string refused)
    {
        audit.Decisions.Add(situation == "waiting" ? Asked() with { Delivery = DecisionDelivery.LeftToHuman } : Asked());
        audit.Working[parentSession] = situation == "stranger" ? JobId.New() : parent;
        answers.Refusal = situation == "beyond" ? PolicyError.BeyondParent : Option<PolicyError>.None;

        await CallAsync(situation == "malformed" ? "maybe" : "allow");

        var result = Assert.Single(agents.Results).Result;
        Assert.True(result.IsError);
        Assert.Contains($"\"refused\":\"{refused}\"", result.Content, StringComparison.Ordinal);
        Assert.Equal(situation == "beyond" ? 1 : 0, answers.Replies.Count);
    }

    private Task CallAsync(string decision, string extra = "") =>
        tool.HandleAsync(
            new AgentActivity(new ToolCalled(parentSession, TurnId.New(), new ItemId("answer"), AnswerChildTool.Name, $$"""{ "child": "{{child.Value}}", "request": "migrate", "decision": "{{decision}}"{{extra}} }""")),
            Cancellation).AsTask();

    private PolicyDecision Asked() =>
        new(childSession, TurnId.New(), new ItemId("migrate"), child, ItemKind.Command, Migration, PolicyAnswer.Ask, Option<PolicyRule>.None, DecisionDelivery.LeftToParent, clock.GetUtcNow())
        {
            Parent = parent,
        };

    private DelegationRecord Record(JobId job) =>
        new(parentSession, new ItemId("delegate-migrate"), "Migrate the database\nwith care", clock.GetUtcNow())
        {
            Parent = parent,
            Child = job,
            Escalation = new ParentEscalation(Window),
        };

    private Task DelegatedAsync() => book.KeepAsync(Record(child), Cancellation);
}
