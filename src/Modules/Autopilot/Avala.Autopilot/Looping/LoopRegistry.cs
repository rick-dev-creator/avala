using System.Collections.Immutable;
using Avala.Autopilot.Contracts;
using Avala.Autopilot.Loops;
using Avala.Autopilot.Sourcing;
using Avala.Sdk;

namespace Avala.Autopilot.Looping;

internal sealed class LoopRegistry(LoopSteps steps, LoopJournal journal, TimeProvider clock) : IAutopilot, IAsyncDisposable
{
    private ImmutableDictionary<LoopId, LoopRunner> runners = ImmutableDictionary<LoopId, LoopRunner>.Empty;

    public IEnumerable<LoopRunner> Running => Volatile.Read(ref runners).Values.Where(runner => !runner.Ended);

    public async ValueTask<Result<LoopId, AutopilotError>> StartAsync(LoopRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Repository))
        {
            return AutopilotError.EmptyRepository;
        }

        if (!Valid(request))
        {
            return AutopilotError.InvalidLimits;
        }

        var loop = LoopRecord.Begin(LoopId.New(), request with { Repository = RepositoryKey.Of(request.Repository) }, clock.GetUtcNow());
        var runner = new LoopRunner(loop, steps, journal, clock);
        var added = false;
        ImmutableInterlocked.Update(ref runners, current =>
        {
            added = !current.Values.Any(other => other.Repository == runner.Repository && !other.Ended);

            return added ? current.Add(runner.Id, runner) : current;
        });

        if (!added)
        {
            await runner.DisposeAsync();

            return AutopilotError.AlreadyRunning;
        }

        journal.Keep(loop);
        runner.Start();

        return runner.Id;
    }

    public ValueTask<Result<LoopId, AutopilotError>> PauseAsync(LoopId loop, CancellationToken cancellationToken) =>
        CommandAsync(loop, runner => runner.PauseAsync(cancellationToken));

    public ValueTask<Result<LoopId, AutopilotError>> ResumeAsync(LoopId loop, CancellationToken cancellationToken) =>
        CommandAsync(loop, runner => runner.ResumeAsync(cancellationToken));

    public ValueTask<Result<LoopId, AutopilotError>> StopAsync(LoopId loop, CancellationToken cancellationToken) =>
        CommandAsync(loop, runner => runner.StopAsync(cancellationToken));

    public IReadOnlyList<LoopState> Loops() => journal.Book.States();

    public Option<LoopDigest> DigestOf(LoopId loop) => journal.Book.DigestOf(loop);

    public async ValueTask DisposeAsync()
    {
        foreach (var runner in Volatile.Read(ref runners).Values)
        {
            await runner.DisposeAsync();
        }
    }

    private async ValueTask<Result<LoopId, AutopilotError>> CommandAsync(LoopId loop, Func<LoopRunner, Task<Result<LoopId, AutopilotError>>> command) =>
        Volatile.Read(ref runners).TryGetValue(loop, out var runner) ? await command(runner) : AutopilotError.UnknownLoop;

    private static bool Valid(LoopRequest request)
    {
        var limits = request.Limits;

        return request.AttemptsPerRound > 0
            && limits.FailuresInARow > 0
            && limits.SameFailure > 0
            && limits.NothingChanged > 0
            && limits.Iterations > 0
            && limits.PauseAtLimit is > 0 and <= 1
            && limits.Window > TimeSpan.Zero
            && limits.SpendPerLoop.Concat(limits.SpendPerWindow).All(cap => cap.Amount > 0 && !string.IsNullOrWhiteSpace(cap.Currency));
    }
}
