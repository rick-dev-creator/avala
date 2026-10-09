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

public sealed class FormDecisionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);

    private static readonly AgentForm Question = new(
        FormPurpose.Question,
        "Choose a database",
        "The service needs storage.",
        [new FormField("database", "Database", "Which database?", FieldKind.SingleChoice, [new FormOption("SQLite", "A file.", Recommended: true)])]);

    private readonly GovernanceBook book = new(new Governance.InMemoryGovernance());
    private readonly RecordingBus bus = new();
    private readonly AnsweringAgents agents = new();
    private readonly SessionId session = SessionId.New();
    private readonly TurnId turn = TurnId.New();
    private readonly JobId job = JobId.New();
    private readonly SessionGovernor governor;

    public FormDecisionTests() =>
        governor = new SessionGovernor(book, new NoPolicyFiles(), new PermissionResponder(agents, new SymbolicLinks(), new FakeTimeProvider(Now)), bus);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ASupervisedSessionLeavesAFormToAHumanAndRecordsItAsync()
    {
        Govern(Autonomy.Supervised);

        await AskAsync();

        Assert.Empty(agents.Answers);
        var decision = Decided();
        Assert.Equal(
            (DecisionDelivery.LeftToHuman, Autonomy.Supervised, true, 0, Option<JobId>.Some(job)),
            (decision.Delivery, decision.Autonomy, decision.Answer.IsNone, decision.Assumptions.Count, decision.Job));
        Assert.Equal([decision], book.FormsOfJob(job));
    }

    [Fact]
    public async Task AnAutonomousSessionAnswersAFormWithTheRecommendedOptionAndRecordsTheAssumptionAsync()
    {
        Govern(Autonomy.Autonomous);

        await AskAsync();

        var (answeredIn, answer) = Assert.Single(agents.Answers);
        Assert.Equal((session, new ItemId("question")), (answeredIn, answer.Item));
        Assert.Equal(["SQLite"], Assert.Single(answer.Fields).Chosen);
        var decision = Decided();
        Assert.Equal((DecisionDelivery.Answered, Autonomy.Autonomous, Now), (decision.Delivery, decision.Autonomy, decision.At));
        Assert.Equal(Option<FormAnswer>.Some(answer), decision.Answer);
        var assumed = Assert.Single(decision.Assumptions);
        Assert.Equal(("database", "Which database?", AssumptionBasis.RecommendedOption), (assumed.Field, assumed.Prompt, assumed.Basis));
        Assert.Equal([decision], book.FormsOfSession(session));
        Assert.Equal([decision], book.FormsOfJob(job));
    }

    [Fact]
    public async Task AnAutomaticAnswerTheAgentRejectsIsRecordedAsUndeliveredAsync()
    {
        Govern(Autonomy.Autonomous);
        agents.Rejection = AgentError.NoPendingForm;

        await AskAsync();

        Assert.Equal(DecisionDelivery.Undelivered, Decided().Delivery);
    }

    private void Govern(Autonomy autonomy)
    {
        var policy = new PermissionPolicy([], autonomy, FormStrategy.Recommended);
        book.Keep(book.Of(session).OpenedIn("/worktrees/1", policy, new SessionPolicy(session, PolicyFileStatus.Applied, Option<PolicyError>.None, [], CommittedFiles.Origin())).WorkingOn(job));
    }

    private Task AskAsync() =>
        governor.HandleAsync(new AgentActivity(new FormRequested(session, turn, new ItemId("question"), Question)), Cancellation).AsTask();

    private FormDecision Decided() => Assert.IsType<FormDecided>(Assert.Single(bus.Published)).Decision;
}
