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
    private readonly Option<string> effort;
    private Option<Stamp> live;
    private Option<Guid> conversation;
    private decimal spent;
    private bool tokenIssued;
    private bool modelReported;
    private bool interrupting;
    private bool rejected;
    private int interruptions;
    private int stale;

    public Conversation(SessionId session, SessionOptions options, Places places, Option<ConversationMark> resumed)
    {
        var tools = new ToolBook(options.Tools);
        this.session = session;
        translator = new StreamTranslator(tools, places);
        desk = new ControlDesk(tools, options.Permissions, places);
        conversation = resumed.Map(mark => mark.Session);
        spent = resumed.Match(mark => mark.Spent, () => 0m);
        effort = ClaudeModels.Chosen(options).Effort;
    }

    public Option<TurnId> Live => live.Map(stamp => stamp.Turn);

    public Conversation Recalling(IEnumerable<JsonNode> earlier)
    {
        foreach (var message in earlier)
        {
            translator.Recall(message);
        }

        return this;
    }

    public Result<(TurnId Turn, Reaction Reaction), AgentError> Begin(UserTurn turn)
    {
        if (turn.MidTurn)
        {
            return live.ToResult(AgentError.NoTurnInProgress).Map(running =>
                (running.Turn, new Reaction([new MessageQueued(session, running.Turn, turn.Text)], [Messages.User(turn, commands.Send(midTurn: true))])));
        }

        if (live.IsSome)
        {
            return AgentError.TurnInProgress;
        }

        var stamp = new Stamp(session, TurnId.New());
        live = stamp;
        tokenIssued = false;
        modelReported = false;
        rejected = false;
        context.Begin();

        return (stamp.Turn, new Reaction([new TurnStarted(session, stamp.Turn)], [Messages.User(turn, commands.Send(midTurn: false))]));
    }

    public Reaction Receive(JsonNode message) => message.TextOr("type", string.Empty) switch
    {
        "system" when message.TextOr("subtype", string.Empty) == "init" => Initialized(message),
        "control_request" => desk.Receive(message, live),
        "control_cancel_request" => desk.Cancel(message, live),
        "command_lifecycle" => Tracked(message),
        "result" when stale > 0 => Swallowed(),
        var type => live.Match(stamp => During(type, message, stamp), () => Reaction.None),
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
        commands.Report(lifecycle);

        return Reaction.None;
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
