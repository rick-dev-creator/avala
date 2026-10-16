using System.Text.Json.Nodes;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.ClaudeCode.Protocol;
using Avala.Sdk;

namespace Avala.ClaudeCode.Conversations;

internal sealed class Conversation
{
    private readonly SessionId session;
    private readonly StreamTranslator translator;
    private readonly ControlDesk desk;
    private readonly Commands commands = new();
    private readonly ContextGauge context = new();
    private readonly RemoteBridge remote;
    private readonly Option<string> effort;
    private Option<Stamp> live;
    private Option<Guid> conversation;
    private decimal spent;
    private bool tokenIssued;
    private bool modelReported;
    private bool interrupting;
    private bool rejected;
    private bool unsaid;
    private int interruptions;
    private int stale;

    public Conversation(SessionId session, SessionOptions options, Places places, Option<ConversationMark> resumed)
    {
        var tools = new ToolBook(options.Tools);
        this.session = session;
        translator = new StreamTranslator(tools, places);
        desk = new ControlDesk(tools, options.Permissions, places);
        remote = new RemoteBridge(session);
        conversation = resumed.Map(mark => mark.Session);
        spent = resumed.Match(mark => mark.Spent, () => 0m);
        effort = ClaudeModels.Chosen(options).Effort;
    }

    public Option<TurnId> Live => live.Map(stamp => stamp.Turn);

    public RemoteState Remote => remote.State;

    public Conversation Recalling(IEnumerable<JsonNode> earlier)
    {
        foreach (var message in earlier)
        {
            translator.Recall(message);
        }

        return this;
    }

    public Result<(TurnId Turn, Reaction Reaction), AgentError> Begin(UserTurn turn) => Begin(turn, []);

    public Result<(TurnId Turn, Reaction Reaction), AgentError> Begin(UserTurn turn, IReadOnlyList<JsonNode> attachments)
    {
        if (turn.MidTurn)
        {
            return live.ToResult(AgentError.NoTurnInProgress).Map(running =>
                (running.Turn, new Reaction([new MessageQueued(session, running.Turn, turn.Text) { Attachments = turn.Attachments }], [Messages.User(turn, commands.Send(midTurn: true), attachments)])));
        }

        if (live.IsSome)
        {
            return AgentError.TurnInProgress;
        }

        var stamp = Opened();

        return (stamp.Turn, new Reaction([new TurnStarted(session, stamp.Turn)], [Messages.User(turn, commands.Send(midTurn: false), attachments)]));
    }

    public Reaction ControlRemotely(bool on) => remote.Turn(on);

    public Reaction Receive(JsonNode message) => message.TextOr("type", string.Empty) switch
    {
        "system" when message.TextOr("subtype", string.Empty) == "init" => Initialized(message),
        "control_request" => Requested(message),
        "control_cancel_request" => desk.Cancel(message, live),
        "control_response" when RemoteBridge.Answers(message) => remote.Answered(message),
        "system" when message.TextOr("subtype", string.Empty) == "bridge_state" => remote.Reported(message),
        "user" when RemoteBridge.FromDevice(message) => Arrived(message),
        "command_lifecycle" => Tracked(message),
        "result" when stale > 0 => Swallowed(),
        var type => Continued(type, message),
    };

    public Result<Reaction, AgentError> Respond(PermissionDecision decision) =>
        live.ToResult(AgentError.NoPendingPermission).Bind(stamp => desk.Respond(decision, stamp));

    public Result<Reaction, AgentError> Answer(FormAnswer answer) =>
        live.ToResult(AgentError.NoPendingForm).Bind(stamp => desk.Answer(answer, stamp));

    public Result<Reaction, AgentError> Return(ToolResult result) =>
        live.ToResult(AgentError.NoPendingCall).Bind(stamp => desk.Return(result, stamp));

    public Result<(TurnId Turn, Reaction Reaction), AgentError> Interrupt() =>
        live.ToResult(AgentError.NoTurnInProgress).Map(stamp =>
        {
            if (!commands.Owed)
            {
                return (stamp.Turn, Closed(stamp, TurnOutcome.Interrupted, Reaction.None));
            }

            interrupting = true;

            return (stamp.Turn, desk.Release(interrupted: true).Then(Reaction.Send(Messages.Interrupt(++interruptions))));
        });

    private Reaction Requested(JsonNode request)
    {
        var opening = ControlDesk.CallsTool(request) ? Woken() : Reaction.None;

        return opening.Then(desk.Receive(request, live));
    }

    private Reaction Continued(string type, JsonNode message)
    {
        var opening = type is "assistant" or "stream_event" ? Woken() : Reaction.None;

        return opening.Then(live.Match(stamp => During(type, message, stamp), () => Reaction.None));
    }

    private Reaction During(string type, JsonNode message, Stamp stamp) => type switch
    {
        "assistant" when stale == 0 => Said(message, stamp),
        "stream_event" or "user" when stale == 0 => translator.Receive(message, stamp),
        "system" when message.TextOr("subtype", string.Empty) == "compact_boundary" => Compacted(),
        "rate_limit_event" => Limited(message, stamp),
        "result" => commands.Pending && !interrupting ? Carried(message, stamp) : Ended(message, stamp),
        _ => Reaction.None,
    };

    private Reaction Said(JsonNode message, Stamp stamp)
    {
        context.Saw(message);

        return translator.Receive(message, stamp);
    }

    private Reaction Compacted()
    {
        context.Compacted();

        return Reaction.None;
    }

    private Reaction Limited(JsonNode message, Stamp stamp)
    {
        rejected |= Telemetry.Rejected(message);

        return Reaction.Of([.. Telemetry.Limits(message).Select(limit => new LimitReported(session, stamp.Turn, limit))]);
    }

    private Reaction Initialized(JsonNode message)
    {
        var known = Guid.TryParse(message.TextOr("session_id", string.Empty), out var id) ? id : Option<Guid>.None;
        var changed = known.IsSome && known != conversation;
        conversation = known.IsSome ? known : conversation;

        return live.Match(
            stamp =>
            {
                var model = Ran(message, stamp);

                if (tokenIssued && !changed)
                {
                    return model;
                }

                tokenIssued = conversation.IsSome;

                return Token(stamp).Then(model);
            },
            () => Reaction.None);
    }

    private Reaction Ran(JsonNode message, Stamp stamp)
    {
        var model = message.TextOr("model", string.Empty);

        if (modelReported || model.Length == 0)
        {
            return Reaction.None;
        }

        modelReported = true;

        return Reaction.Of(new ModelReported(session, stamp.Turn, model) { Effort = effort });
    }

    private Reaction Tracked(JsonNode lifecycle)
    {
        var foreign = !commands.Ours(lifecycle);
        commands.Report(lifecycle);

        return foreign && live.IsNone && lifecycle.TextOr("state", string.Empty) == "started" ? BeganRemotely() : Reaction.None;
    }

    private Stamp Opened()
    {
        var stamp = new Stamp(session, TurnId.New());
        live = stamp;
        tokenIssued = false;
        modelReported = false;
        rejected = false;
        unsaid = false;
        context.Begin();

        return stamp;
    }

    private Reaction BeganRemotely()
    {
        var started = Started();
        unsaid = true;

        return started;
    }

    private Reaction Woken() => live.IsNone && stale == 0 ? Started() : Reaction.None;

    private Reaction Started() => Reaction.Of(new TurnStarted(session, Opened().Turn));

    private Reaction Arrived(JsonNode user)
    {
        var opening = live.IsNone ? BeganRemotely() : Reaction.None;

        return opening.Then(live.Match(
            stamp =>
            {
                var began = unsaid;
                unsaid = false;

                return Reaction.Of(new RemoteMessageArrived(session, stamp.Turn, RemoteBridge.Said(user)) { BeganTurn = began });
            },
            () => Reaction.None));
    }

    private Reaction Carried(JsonNode result, Stamp stamp)
    {
        commands.Answered();

        return translator.Close(stamp, interrupted: false)
            .Then(Reaction.Of(new UsageReported(session, stamp.Turn, Telemetry.Tokens(result), Spent(result))))
            .Then(context.Report(session, stamp.Turn, result));
    }

    private Reaction Swallowed()
    {
        stale--;
        commands.Answered();

        return Reaction.None;
    }

    private Option<Cost> Spent(JsonNode result)
    {
        var total = Telemetry.TotalCost(result);
        var cost = total.Map(amount => new Cost(Math.Max(0m, amount - spent), Telemetry.Currency));
        spent = total.Match(amount => amount, () => spent);

        return cost;
    }

    private Reaction Ended(JsonNode result, Stamp stamp)
    {
        var cost = Spent(result);
        var outcome = interrupting ? TurnOutcome.Interrupted : Outcome(result);

        return Closed(stamp, outcome, Reaction.Of(new UsageReported(session, stamp.Turn, Telemetry.Tokens(result), cost)).Then(context.Report(session, stamp.Turn, result)));
    }

    private Reaction Closed(Stamp stamp, TurnOutcome outcome, Reaction usage)
    {
        var interrupted = outcome == TurnOutcome.Interrupted;
        var left = commands.Abandon();
        stale = interrupting ? left : 0;
        var closing = desk.Release(interrupted).Then(translator.Close(stamp, interrupted));
        live = Option<Stamp>.None;
        interrupting = false;

        return closing
            .Then(usage)
            .Then(Token(stamp))
            .Then(Reaction.Of(new TurnCompleted(session, stamp.Turn, outcome)));
    }

    private TurnOutcome Outcome(JsonNode result)
    {
        if (result.TextOr("subtype", string.Empty) == "success" && !result.Flag("is_error"))
        {
            return TurnOutcome.Finished;
        }

        return rejected ? TurnOutcome.LimitReached : TurnOutcome.Failed;
    }

    private Reaction Token(Stamp stamp) =>
        conversation.Match(
            id => Reaction.Of(new ResumeTokenIssued(session, stamp.Turn, new ConversationMark(id, spent).Token)),
            () => Reaction.None);
}
