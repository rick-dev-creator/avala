using System.Threading.Channels;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;
using Avala.Simulator.Domain;

namespace Avala.Simulator.Application;

internal sealed class SimulatedSession : IAgentSession
{
    private readonly Channel<IAgentEvent> events = Channel.CreateUnbounded<IAgentEvent>();
    private readonly CancellationTokenSource lifetime = new();
    private readonly PermissionGate permissions = new();
    private readonly Lock gate = new();
    private readonly Performer performer;
    private readonly Pacing pacing;
    private Scenario? scenario;
    private int played;
    private Act? act;
    private bool closed;
    private Task conducting = Task.CompletedTask;

    public SimulatedSession(SessionOptions options, IFileWriter files, Pacing pacing)
    {
        performer = new Performer(options.WorkingDirectory, files, permissions);
        this.pacing = pacing;
    }

    public SessionId Id { get; } = SessionId.New();

    public IAsyncEnumerable<IAgentEvent> Events => events.Reader.ReadAllAsync(CancellationToken.None);

    public ValueTask<Result<TurnId, AgentError>> SendAsync(UserTurn turn, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Begin(turn));

    public ValueTask<Result<ItemId, AgentError>> RespondAsync(PermissionDecision decision, CancellationToken cancellationToken) =>
        ValueTask.FromResult(permissions.Respond(decision));

    public async ValueTask<Result<TurnId, AgentError>> InterruptAsync(CancellationToken cancellationToken)
    {
        Act? running;

        lock (gate)
        {
            running = act;
        }

        if (running is null)
        {
            return AgentError.NoTurnInProgress;
        }

        await running.Interruption.CancelAsync();

        return running.Turn;
    }

    public async ValueTask DisposeAsync()
    {
        lock (gate)
        {
            closed = true;
        }

        await lifetime.CancelAsync();
        events.Writer.TryComplete();
        await conducting;
        lifetime.Dispose();
    }

    private Result<TurnId, AgentError> Begin(UserTurn turn)
    {
        Act next;
        IReadOnlyList<IStep> script;

        lock (gate)
        {
            if (closed)
            {
                return AgentError.SessionClosed;
            }

            if (act is not null)
            {
                return AgentError.TurnInProgress;
            }

            scenario ??= Scenarios.Choose(turn.Text);
            script = scenario.Script(played++);
            act = next = new Act(TurnId.New(), CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token));
        }

        conducting = ConductAsync(next, script);

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

                if (Publish(current, cue))
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
            Publish(current, cues.Ended(TurnOutcome.Interrupted));
        }
        catch (Exception failure)
        {
            lock (gate)
            {
                closed = true;
                act = null;
            }

            events.Writer.TryComplete(failure);
        }
    }

    private bool Publish(Act current, IAgentEvent cue)
    {
        var ends = cue is TurnCompleted;

        if (ends)
        {
            lock (gate)
            {
                act = ReferenceEquals(act, current) ? null : act;
            }
        }

        events.Writer.TryWrite(cue);

        return ends;
    }

    private sealed record Act(TurnId Turn, CancellationTokenSource Interruption);
}
