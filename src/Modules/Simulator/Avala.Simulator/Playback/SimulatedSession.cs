using System.Threading.Channels;
using Avala.Agents.Contracts.Capabilities;
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
    private readonly Gates gates;
    private readonly Performer performer;
    private readonly SessionOptions options;
    private readonly Stagecraft craft;
    private readonly Adaptation adaptation;
    private readonly bool midTurn;
    private Option<Conversation> conversation;
    private Option<Act> act;
    private bool closed;
    private Task conducting = Task.CompletedTask;

    public SimulatedSession(SessionOptions options, Stagecraft craft, Option<AgentAccount> account, Option<Conversation> conversation)
        : this(options, craft, new Casting(account, SimulatedCapabilities.On(options.Connection)), conversation)
    {
    }

    public SimulatedSession(SessionOptions options, Stagecraft craft, Casting casting, Option<Conversation> conversation)
    {
        var (account, capabilities) = casting;
        gates = new Gates(
            new ReplyGate<PermissionDecision>(stage, AgentError.NoPendingPermission),
            new ReplyGate<FormAnswer>(stage, AgentError.NoPendingForm),
            new ReplyGate<ToolResult>(stage, AgentError.NoPendingCall),
            Channel.CreateUnbounded<string>());
        midTurn = capabilities.Has<AcceptsMessagesMidTurn>();
        performer = new Performer(options, craft, gates, capabilities);
        adaptation = new Adaptation(capabilities);
        this.options = options;
        this.craft = craft;
        this.conversation = conversation;
        Account = account;
    }

    public SessionId Id { get; } = SessionId.New();

    public Option<AgentAccount> Account { get; }

    public IAsyncEnumerable<IAgentEvent> Events => events.Reader.ReadAllAsync(CancellationToken.None);

    public async ValueTask<Result<TurnId, AgentError>> SendAsync(UserTurn turn, CancellationToken cancellationToken) =>
        await stage.RunAsync(token => turn.MidTurn ? Task.FromResult(Queue(turn)) : BeginAsync(turn, token), cancellationToken);

    public async ValueTask<Result<ItemId, AgentError>> RespondAsync(PermissionDecision decision, CancellationToken cancellationToken) =>
        await gates.Permissions.RespondAsync(decision.Item, decision, cancellationToken);

    public async ValueTask<Result<ItemId, AgentError>> AnswerAsync(FormAnswer answer, CancellationToken cancellationToken) =>
        await gates.Forms.RespondAsync(answer.Item, answer, cancellationToken);

    public async ValueTask<Result<ItemId, AgentError>> ReturnAsync(ToolResult result, CancellationToken cancellationToken) =>
        await gates.Tools.RespondAsync(result.Item, result, cancellationToken);

    public async ValueTask<Result<TurnId, AgentError>> InterruptAsync(CancellationToken cancellationToken) =>
        await (await stage.RunAsync(_ => Task.FromResult(act.Map(running => (running.Turn, running.Interruption, Conducted: conducting))), cancellationToken)).Match(
            async running =>
            {
                await running.Interruption.CancelAsync();
                await running.Conducted;

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

    private Result<TurnId, AgentError> Queue(UserTurn turn) =>
        closed ? AgentError.SessionClosed
        : !midTurn ? AgentError.Unsupported
        : act.Match(
            running =>
            {
                events.Writer.TryWrite(new MessageQueued(Id, running.Turn, turn.Text));
                gates.Messages.Writer.TryWrite(turn.Text);

                return Result<TurnId, AgentError>.Success(running.Turn);
            },
            () => Result<TurnId, AgentError>.Failure(AgentError.NoTurnInProgress));

    private async Task<Result<TurnId, AgentError>> BeginAsync(UserTurn turn, CancellationToken cancellationToken)
    {
        if (closed)
        {
            return AgentError.SessionClosed;
        }

        if (act.IsSome)
        {
            return AgentError.TurnInProgress;
        }

        var current = await conversation.Match(
            known => Task.FromResult(known),
            async () => Conversation.Begin(await craft.Library.ChooseAsync(turn.Text, options, cancellationToken), SimulatedAccounts.Holder(Account)));
        var next = new Act(TurnId.New(), CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token), turn.Text);
        conversation = current.Advanced;
        act = next;
        conducting = Task.Run(() => ConductAsync(next, current), CancellationToken.None);

        return next.Turn;
    }

    private async Task ConductAsync(Act current, Conversation played)
    {
        var cues = new Cues(Id, current.Turn) { Told = current.Told };
        var interruption = current.Interruption.Token;
        var ended = false;

        try
        {
            await foreach (var cue in performer.PlayAsync(cues, played, interruption))
            {
                foreach (var adapted in adaptation.Adapt(cue))
                {
                    await craft.Pacing.WaitAsync(interruption);
                    ended |= await PublishAsync(current, adapted);
                }
            }

            if (performer.Closing)
            {
                await HangUpAsync(Option<Exception>.None);
            }
            else if (!ended)
            {
                await interruption.UntilCancelledAsync();
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException) when (performer.Expects(played))
        {
            await PublishAsync(current, cues.Ended(TurnOutcome.Interrupted));
        }
        catch (OperationCanceledException)
        {
            foreach (var cue in cues.Diverged(Divergence.Interrupted, started: true))
            {
                await PublishAsync(current, cue);
            }

            await HangUpAsync(Option<Exception>.None);
        }
        catch (Exception failure)
        {
            await HangUpAsync(failure);
        }
    }

    private async Task HangUpAsync(Option<Exception> failure)
    {
        await stage.RunAsync(
            _ =>
            {
                closed = true;
                act = Option<Act>.None;

                return Task.CompletedTask;
            },
            CancellationToken.None);

        events.Writer.TryComplete(failure.Match<Exception?>(exception => exception, () => null));
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

    private sealed record Act(TurnId Turn, CancellationTokenSource Interruption, string Told);
}

internal sealed record Casting(Option<AgentAccount> Account, CapabilitySet Declared);
