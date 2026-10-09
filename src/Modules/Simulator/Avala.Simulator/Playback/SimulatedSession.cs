using System.Threading.Channels;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;
using Avala.Simulator.Scenarios;

namespace Avala.Simulator.Playback;

internal sealed class SimulatedSession : IAgentSession
{
    private readonly Channel<IAgentEvent> events = Channel.CreateUnbounded<IAgentEvent>();
    private readonly CancellationTokenSource lifetime = new();
    private readonly SerialExecutor stage = new();
    private readonly PermissionGate permissions;
    private readonly Performer performer;
    private readonly Pacing pacing;
    private Option<Scenario> scenario;
    private int played;
    private Option<Act> act;
    private bool closed;
    private Task conducting = Task.CompletedTask;

    public SimulatedSession(SessionOptions options, IFileWriter files, Pacing pacing)
    {
        permissions = new PermissionGate(stage);
        performer = new Performer(options, files, permissions);
        this.pacing = pacing;
    }

    public SessionId Id { get; } = SessionId.New();

    public IAsyncEnumerable<IAgentEvent> Events => events.Reader.ReadAllAsync(CancellationToken.None);

    public async ValueTask<Result<TurnId, AgentError>> SendAsync(UserTurn turn, CancellationToken cancellationToken) =>
        await stage.RunAsync(_ => Task.FromResult(Begin(turn)), cancellationToken);

    public async ValueTask<Result<ItemId, AgentError>> RespondAsync(PermissionDecision decision, CancellationToken cancellationToken) =>
        await permissions.RespondAsync(decision, cancellationToken);

    public async ValueTask<Result<TurnId, AgentError>> InterruptAsync(CancellationToken cancellationToken) =>
        await (await stage.RunAsync(_ => Task.FromResult(act), cancellationToken)).Match(
            async running =>
            {
                await running.Interruption.CancelAsync();

                return Result<TurnId, AgentError>.Success(running.Turn);
            },
            () => Task.FromResult(Result<TurnId, AgentError>.Failure(AgentError.NoTurnInProgress)));

    public async ValueTask DisposeAsync()
    {
        var conducted = await stage.RunAsync(
            _ =>
            {
                closed = true;

                return Task.FromResult(conducting);
            },
            CancellationToken.None);

        await lifetime.CancelAsync();
        events.Writer.TryComplete();
        await conducted;
        await stage.DisposeAsync();
        lifetime.Dispose();
    }

    private Result<TurnId, AgentError> Begin(UserTurn turn)
    {
        if (closed)
        {
            return AgentError.SessionClosed;
        }

        if (act.IsSome)
        {
            return AgentError.TurnInProgress;
        }

        var chosen = scenario.Match(known => known, () => ScenarioCatalog.Choose(turn.Text));
        var next = new Act(TurnId.New(), CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token));
        scenario = chosen;
        act = next;
        conducting = Task.Run(() => ConductAsync(next, chosen.Script(played++)), CancellationToken.None);

        return next.Turn;
    }

    private async Task ConductAsync(Act current, IReadOnlyList<IStep> script)
    {
        var cues = new Cues(Id, current.Turn);
        var interruption = current.Interruption.Token;

        try
        {
            await foreach (var cue in performer.PlayAsync(cues, script, interruption))
            {
                await pacing.WaitAsync(interruption);

                if (await PublishAsync(current, cue))
                {
                    return;
                }
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, interruption);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException)
        {
            await PublishAsync(current, cues.Ended(TurnOutcome.Interrupted));
        }
        catch (Exception failure)
        {
            await stage.RunAsync(
                _ =>
                {
                    closed = true;
                    act = Option<Act>.None;

                    return Task.CompletedTask;
                },
                CancellationToken.None);

            events.Writer.TryComplete(failure);
        }
    }

    private async Task<bool> PublishAsync(Act current, IAgentEvent cue)
    {
        var ends = cue is TurnCompleted;

        if (ends)
        {
            await stage.RunAsync(
                _ =>
                {
                    act = act.Bind(running => ReferenceEquals(running, current) ? Option<Act>.None : running);

                    return Task.CompletedTask;
                },
                CancellationToken.None);
        }

        events.Writer.TryWrite(cue);

        return ends;
    }

    private sealed record Act(TurnId Turn, CancellationTokenSource Interruption);
}
