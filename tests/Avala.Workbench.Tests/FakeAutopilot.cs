using Avala.Agents.Contracts.Events;
using Avala.Autopilot.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Workbench.Tests;

internal sealed class FakeAutopilot : IAutopilot
{
    public static readonly LoopId Running = new(Guid.Parse("01a12000-0000-7000-8000-00000000000a"));

    public static readonly LoopId Tripped = new(Guid.Parse("01a12000-0000-7000-8000-00000000000b"));

    public List<LoopDigest> Digests { get; } =
    [
        Digest(
            new LoopState(Tripped, "/work/billing", LoopStatus.Ended, FakeTriggers.Now.AddHours(-3), 2, Option<JobId>.None)
            {
                Ending = LoopEnding.BreakerTripped,
                Breaker = Breaker.SameFailure,
            },
            [Iteration(1, "retry", IterationOutcome.NeedsHelp), Iteration(2, "retry-docs", IterationOutcome.NeedsHelp)])
        with
        {
            Breakers = [new BreakerTrip(Breaker.SameFailure, "Check tests Failed exit 1", 2m, 2m, FakeTriggers.Now)],
        },
        Digest(
            new LoopState(Running, "/work/ledger-api", LoopStatus.Running, FakeTriggers.Now.AddHours(-1), 3, JobId.New()) { Limits = new LoopLimits { Iterations = 20 } },
            [
                Iteration(1, "greet", IterationOutcome.ApprovedAutomatically) with { Cost = [new Cost(0.12m, "USD")] },
                Iteration(2, "deps", IterationOutcome.ApprovedAutomatically) with { Cost = [new Cost(0.2m, "USD")] },
                Iteration(3, "docs", IterationOutcome.AwaitingReview) with { Exceptions = [ExceptionReason.VerificationNotPassed, ExceptionReason.RuleFileEdited], Cost = [new Cost(0.1m, "USD")] },
            ]),
    ];

    public List<(string Command, LoopId Loop)> Commands { get; } = [];

    public Option<AutopilotError> Refusal { get; set; }

    public ValueTask<Result<LoopId, AutopilotError>> StartAsync(LoopRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Result<LoopId, AutopilotError>.Failure(AutopilotError.AlreadyRunning));

    public ValueTask<Result<LoopId, AutopilotError>> PauseAsync(LoopId loop, CancellationToken cancellationToken) => CommandAsync("pause", loop);

    public ValueTask<Result<LoopId, AutopilotError>> ResumeAsync(LoopId loop, CancellationToken cancellationToken) => CommandAsync("resume", loop);

    public ValueTask<Result<LoopId, AutopilotError>> StopAsync(LoopId loop, CancellationToken cancellationToken) => CommandAsync("stop", loop);

    public IReadOnlyList<LoopState> Loops() => [.. Digests.Select(digest => digest.State)];

    public Option<LoopDigest> DigestOf(LoopId loop) => Digests.FirstOrDefault(digest => digest.State.Loop == loop).ToOption();

    private ValueTask<Result<LoopId, AutopilotError>> CommandAsync(string command, LoopId loop)
    {
        Commands.Add((command, loop));

        return ValueTask.FromResult(Refusal.Match(Result<LoopId, AutopilotError>.Failure, () => Result<LoopId, AutopilotError>.Success(loop)));
    }

    private static LoopDigest Digest(LoopState state, IReadOnlyList<IterationRecord> iterations) =>
        new(state, iterations, [], [], [], [.. iterations.SelectMany(iteration => iteration.Cost).GroupBy(cost => cost.Currency).Select(spent => new Cost(spent.Sum(cost => cost.Amount), spent.Key))]);

    private static IterationRecord Iteration(int number, string key, IterationOutcome outcome) =>
        new(number, new SourcedTask("backlog", key, "/work/ledger-api", key), JobId.New(), outcome, FakeTriggers.Now);
}
