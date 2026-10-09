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
    private readonly ReplyGate<PermissionDecision> permissions;
    private readonly ReplyGate<FormAnswer> forms;
    private readonly Performer performer;
    private readonly Pacing pacing;
    private Option<Conversation> conversation;
    private Option<Act> act;
    private bool closed;
    private Task conducting = Task.CompletedTask;

    public SimulatedSession(SessionOptions options, IFileWriter files, Pacing pacing, Option<Conversation> resumed)
    {
        permissions = new ReplyGate<PermissionDecision>(stage, AgentError.NoPendingPermission);
        forms = new ReplyGate<FormAnswer>(stage, AgentError.NoPendingForm);
        performer = new Performer(options, files, permissions, forms);
        this.pacing = pacing;
        conversation = resumed;
    }

    public static AgentAccount SimulatedAccount { get; } = new("simulated-account", "Simulated account");

    public SessionId Id { get; } = SessionId.New();

    public Option<AgentAccount> Account => SimulatedAccount;

    public IAsyncEnumerable<IAgentEvent> Events => events.Reader.ReadAllAsync(CancellationToken.None);

    public async ValueTask<Result<TurnId, AgentError>> SendAsync(UserTurn turn, CancellationToken cancellationToken) =>
        await stage.RunAsync(_ => Task.FromResult(Begin(turn)), cancellationToken);

    public async ValueTask<Result<ItemId, AgentError>> RespondAsync(PermissionDecision decision, CancellationToken cancellationToken) =>
        await permissions.RespondAsync(decision.Item, decision, cancellationToken);

    public async ValueTask<Result<ItemId, AgentError>> AnswerAsync(FormAnswer answer, CancellationToken cancellationToken) =>
        await forms.RespondAsync(answer.Item, answer, cancellationToken);

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

        var current = conversation.Match(known => known, () => Conversation.Begin(turn.Text));
        var next = new Act(TurnId.New(), CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token));
        conversation = current.Advanced;
        act = next;
        conducting = Task.Run(() => ConductAsync(next, current), CancellationToken.None);

        return next.Turn;
    }

    private async Task ConductAsync(Act current, Conversation played)
    {
        var cues = new Cues(Id, current.Turn);
        var interruption = current.Interruption.Token;

        try
        {
            await foreach (var cue in performer.PlayAsync(cues, played, interruption))
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
