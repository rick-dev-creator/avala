using System.Collections.Concurrent;
using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Autopilot.Contracts;
using Avala.Autopilot.Evidence;
using Avala.Autopilot.FollowUps;
using Avala.Autopilot.Sourcing;
using Avala.Autopilot.Tests.Looping;
using Avala.Autopilot.Tests.Sourcing;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Autopilot.Tests.FollowUps;

public sealed class FollowUpTests
{
    private const string Proposal = """{ "instruction": "Announce the changelog to the team", "reason": "The team should know." }""";

    private static readonly AutopilotRules Accepting = new(ApprovalRule.CleanEvidence, FollowUpRule.Accept);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAutonomousJobsProposalIsQueuedForItsRepositoryWhenItsRulesAcceptFollowUpsAsync()
    {
        var desk = new Desk(Accepting);

        await desk.ProposeAsync(Proposal);

        var queued = Assert.Single(desk.Ledger.All);
        Assert.Equal(
            (FollowUpSource.Source, Pilot.Repository, "Announce the changelog to the team", TaskState.Proposed),
            (queued.Task.Source, queued.Task.Repository, queued.Task.Instruction, queued.State));
        var returned = Assert.Single(desk.Agents.Results);
        Assert.Equal((desk.Session, new ItemId("propose"), false), (returned.Session, returned.Result.Item, returned.Result.IsError));
        Assert.StartsWith("Accepted", returned.Result.Content, StringComparison.Ordinal);
        var decided = Assert.Single(desk.Bus.Published.OfType<FollowUpDecided>()).Decision;
        Assert.Equal((Option<SourcedTask>.Some(queued.Task), Option<FollowUpRefusal>.None, Option<JobId>.Some(desk.Job)), (decided.Task, decided.Refusal, decided.Job));
    }

    [Theory]
    [InlineData("supervised", "NotAutonomous")]
    [InlineData("rules that refuse", "NotAllowed")]
    [InlineData("rules that cannot be read", "UnreadableRules")]
    [InlineData("no job", "NoJob")]
    [InlineData("no instruction", "MalformedInput")]
    [InlineData("not json", "MalformedInput")]
    public async Task AProposalThePolicyRefusesIsAnsweredWithTheReasonAndQueuesNothingAsync(string situation, string refusal)
    {
        var desk = new Desk(situation switch
        {
            "rules that refuse" => AutopilotRules.Default,
            "rules that cannot be read" => AutopilotError.Malformed,
            _ => Accepting,
        });

        await desk.ProposeAsync(
            situation switch
            {
                "no instruction" => """{ "reason": "Because." }""",
                "not json" => "announce it",
                _ => Proposal,
            },
            autonomous: situation != "supervised",
            started: situation != "no job");

        Assert.Empty(desk.Ledger.All);
        var decided = Assert.Single(desk.Bus.Published.OfType<FollowUpDecided>()).Decision;
        Assert.Equal(Option<FollowUpRefusal>.Some(Enum.Parse<FollowUpRefusal>(refusal)), decided.Refusal);
        var returned = Assert.Single(desk.Agents.Results).Result;
        Assert.StartsWith("Refused", returned.Content, StringComparison.Ordinal);
        Assert.Equal(refusal == nameof(FollowUpRefusal.MalformedInput), returned.IsError);
    }

    [Fact]
    public async Task ACallOfAnotherToolIsLeftToItsOwnerAsync()
    {
        var desk = new Desk(Accepting);

        await desk.ProposeAsync(Proposal, tool: "deploy");

        Assert.Empty(desk.Agents.Results);
        Assert.Empty(desk.Bus.Published);
    }

    private sealed class Desk
    {
        private readonly FakeJobs jobs = new();
        private readonly FakeEvidence evidence = new();
        private readonly FollowUpDesk desk;

        public Desk(Result<AutopilotRules, AutopilotError> rules)
        {
            var policy = new FollowUpPolicy(evidence, new FixedRules(rules), jobs, new FakeTimeProvider());
            desk = new FollowUpDesk(policy, Ledger, Agents, Bus);
        }

        public SessionId Session { get; } = SessionId.New();

        public JobId Job { get; private set; }

        public MemoryLedger Ledger { get; } = new();

        public ReturningAgents Agents { get; } = new();

        public RecordingBus Bus { get; } = new();

        public async Task ProposeAsync(string input, bool autonomous = true, bool started = true, string tool = FollowUpTool.Name)
        {
            Job = Outcomes.Succeeds(await jobs.SubmitAsync(new JobRequest(Pilot.Repository, "Start a changelog"), Cancellation));
            var effective = autonomous ? Autonomy.Autonomous : Autonomy.Supervised;
            evidence.Applied(new SessionAutonomy(Session, Job, Autonomy.Autonomous, Option<Autonomy>.None, effective, Refused: false));
            await desk.HandleAsync(new SessionOpened(Session, new ProviderInfo("simulator", "Simulator"), "/worktrees/job-1", new ConnectionName("work")), Cancellation);

            if (started)
            {
                await desk.HandleAsync(new JobSessionStarted(Job, Session), Cancellation);
            }

            await desk.HandleAsync(new AgentActivity(new ToolCalled(Session, TurnId.New(), new ItemId("propose"), tool, input)), Cancellation);
        }
    }

    internal sealed class ReturningAgents : IAgents
    {
        private readonly ConcurrentQueue<(SessionId Session, ToolResult Result)> results = new();

        public IReadOnlyList<(SessionId Session, ToolResult Result)> Results => [.. results];

        public ValueTask<Result<ItemId, AgentError>> ReturnAsync(SessionId session, ToolResult result, CancellationToken cancellationToken)
        {
            results.Enqueue((session, result));

            return ValueTask.FromResult(Result<ItemId, AgentError>.Success(result.Item));
        }

        public ValueTask<Result<OpenedSession, AgentError>> OpenAsync(AgentRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public bool IsOpen(SessionId session) => true;

        public ValueTask<Result<AgentTurn, AgentError>> SendAsync(SessionId session, string message, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<ItemId, AgentError>> RespondAsync(SessionId session, PermissionDecision decision, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<ItemId, AgentError>> AnswerAsync(SessionId session, FormAnswer answer, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<TurnId, AgentError>> InterruptAsync(SessionId session, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<AgentTurn, AgentError>> SteerAsync(SessionId session, string message, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<SessionId, AgentError>> StopAsync(SessionId session, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
