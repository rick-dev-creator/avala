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

public sealed class SharedAnswersTests
{
    private const string Migration = "dotnet ef database update";

    private readonly GovernanceBook book = new(new Governance.InMemoryGovernance());
    private readonly RecordingBus bus = new();
    private readonly AnsweringAgents agents = new();
    private readonly SessionId session = SessionId.New();
    private readonly SessionId rivalSession = SessionId.New();
    private readonly JobId job = JobId.New();
    private readonly JobId rival = JobId.New();
    private readonly SessionGovernor governor;
    private readonly SharedAnswers answers;

    public SharedAnswersTests()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.Zero));
        governor = new SessionGovernor(book, new SessionTerms(new NoPolicyFiles(), []), new PermissionResponder(agents, new RequestPlaces(new SymbolicLinks(), new FixedReadableFolders([])), clock, []), bus);
        var ledger = new AnswerLedger(book, bus, clock, new RecordedRounds());
        answers = new SharedAnswers(new HumanAnswers(book, agents, new RepositoryFiles(), ledger), book, ledger, [new Rivals(job, rival)]);
        book.Keep(book.Of(session).WorkingOn(job));
        book.Keep(book.Of(rivalSession).WorkingOn(rival));
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAnswerForThisContestAnswersTheSameRequestARivalCandidateIsWaitingOnAsync()
    {
        await RequestAsync(session, "migrate");
        await RequestAsync(rivalSession, "migrate-too");

        _ = Outcomes.Succeeds(await answers.AnswerAsync(session, Allow("migrate", Remember.ForThisContest), Cancellation));

        Assert.Equal(
            [(session, PermissionAnswer.Allow), (rivalSession, PermissionAnswer.Allow)],
            agents.Responses.Select(response => (response.Item1, response.Item2.Answer)));
        Assert.Equal([PermissionAnswer.Allow], book.AnswersOfJob(rival).Select(answer => answer.Answer));
    }

    [Fact]
    public async Task AnAnswerForThisContestIsRememberedForARivalCandidateThatHasNotAskedYetAsync()
    {
        await RequestAsync(session, "migrate");

        _ = Outcomes.Succeeds(await answers.AnswerAsync(session, Allow("migrate", Remember.ForThisContest), Cancellation));
        await RequestAsync(rivalSession, "migrate-later");

        Assert.Equal([Remembering.Always(ItemKind.Command, ["dotnet"], PolicyAnswer.Allow, contest: true)], book.JobRulesOf(rival));
        var later = Assert.IsType<PermissionDecided>(bus.Published[^1]).Decision;
        Assert.Equal((rivalSession, PolicyAnswer.Allow, DecisionDelivery.Answered), (later.Session, later.Answer, later.Delivery));
    }

    [Fact]
    public async Task AnAnswerRememberedOnlyForThisJobLeavesARivalCandidateWaitingAsync()
    {
        await RequestAsync(session, "migrate");
        await RequestAsync(rivalSession, "migrate-too");

        _ = Outcomes.Succeeds(await answers.AnswerAsync(session, Allow("migrate", Remember.ForThisJob), Cancellation));

        Assert.Equal([session], agents.Responses.Select(response => response.Item1));
        Assert.Empty(book.JobRulesOf(rival));
    }

    [Fact]
    public async Task EverythingInThisContestRaisesARivalCandidateThatHasNotAskedYetAsync()
    {
        await RequestAsync(session, "migrate");

        _ = Outcomes.Succeeds(await answers.AnswerAsync(session, Allow("migrate", Remember.EverythingInThisContest), Cancellation));

        Assert.Equal([job, rival], bus.Published.OfType<AutonomyApplied>().Select(applied => applied.Autonomy.Job));
        Assert.Equal(Autonomy.Autonomous, book.Of(rivalSession).Policy.Autonomy);
    }

    private static PermissionReply Allow(string item, Remember remember) => new(new ItemId(item), PermissionAnswer.Allow) { Remember = remember };

    private Task RequestAsync(SessionId asking, string item) =>
        governor.HandleAsync(new AgentActivity(new PermissionRequested(asking, TurnId.New(), new ItemId(item), "Run", ItemKind.Command, Migration)), Cancellation).AsTask();

    private sealed class Rivals(params JobId[] candidates) : IAnswerSharing
    {
        public ValueTask<IReadOnlyList<JobId>> SharersAsync(JobId job, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<JobId>>([.. candidates.Where(candidate => candidate != job)]);
    }
}
