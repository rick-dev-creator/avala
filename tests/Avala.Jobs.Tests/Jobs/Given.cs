using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Jobs.Jobs;
using Avala.Testing;
using Avala.Workspaces.Contracts;

namespace Avala.Jobs.Tests.Jobs;

internal static class Given
{
    public static Instruction Instruction { get; } = Outcomes.Succeeds(Avala.Jobs.Jobs.Instruction.Create("Add GitHub login"));

    public static Feedback Feedback { get; } = Outcomes.Succeeds(Avala.Jobs.Jobs.Feedback.Create("Two tests fail"));

    public static RepositoryPath Repository { get; } = Outcomes.Succeeds(RepositoryPath.Create("/repos/shop"));

    public static WorkspaceId Workspace { get; } = WorkspaceId.New();

    public static SessionId Session { get; } = SessionId.New();

    public static ConnectionName Connection { get; } = new("work");

    public static Job Job(int attemptsPerRound = 3) =>
        Outcomes.Succeeds(Avala.Jobs.Jobs.Job.Create(
            JobId.New(),
            Instruction,
            Outcomes.Succeeds(AttemptBudget.Create(attemptsPerRound)),
            Repository,
            Submitted,
            Autonomy.Supervised,
            parent: Parent));

    public static JobId Parent { get; } = JobId.New();

    public static DateTimeOffset Submitted { get; } = new(2026, 10, 9, 8, 0, 0, TimeSpan.Zero);

    public static DateTimeOffset Ended { get; } = new(2026, 10, 9, 9, 30, 0, TimeSpan.Zero);

    public static Job JobIn(JobState state, int attemptsPerRound = 3)
    {
        var job = Job(state == JobState.NeedsHelp ? 1 : attemptsPerRound);

        foreach (var step in PathTo(state))
        {
            step(job);
        }

        return job;
    }

    private static Action<Job>[] PathTo(JobState state) => state switch
    {
        JobState.Draft => [],
        JobState.Preparing => [Submit],
        JobState.Running => [Submit, Start],
        JobState.Checking => [Submit, Start, CompleteTurn],
        JobState.AwaitingReview => [Submit, Start, CompleteTurn, Pass],
        JobState.NeedsHelp => [Submit, Start, CompleteTurn, RequestHelp],
        JobState.Approved => [Submit, Start, CompleteTurn, Pass, Approve],
        JobState.Discarded => [Discard],
        JobState.Failed => [Submit, Fail],
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Not a reachable state"),
    };

    private static void Submit(Job job) => Outcomes.Succeeds(job.Submit());

    private static void Start(Job job) => Outcomes.Succeeds(job.Start(Workspace, Session, Connection));

    private static void CompleteTurn(Job job) => Outcomes.Succeeds(job.CompleteTurn());

    private static void Pass(Job job) => Outcomes.Succeeds(job.Pass());

    private static void RequestHelp(Job job) => Outcomes.Succeeds(job.RequestHelp());

    private static void Approve(Job job) => Outcomes.Succeeds(job.Approve(Ended));

    private static void Discard(Job job) => Outcomes.Succeeds(job.Discard(Ended));

    private static void Fail(Job job) => Outcomes.Succeeds(job.Fail(FailureReason.AgentFailed, Ended));
}
