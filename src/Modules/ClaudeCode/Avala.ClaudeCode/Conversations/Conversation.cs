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
    private readonly Option<string> effort;
    private Option<Stamp> live;
    private Option<Guid> conversation;
    private decimal spent;
    private bool tokenIssued;
    private bool modelReported;
    private bool interrupting;
    private int interruptions;
    private int queued;
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

    public Result<(TurnId Turn, Reaction Reaction), AgentError> Begin(UserTurn turn)
    {
        if (turn.MidTurn)
        {
            return live.ToResult(AgentError.NoTurnInProgress).Map(running =>
            {
                queued++;

                return (running.Turn, new Reaction([new MessageQueued(session, running.Turn, turn.Text)], [Messages.User(turn)]));
            });
        }

        if (live.IsSome)
        {
            return AgentError.TurnInProgress;
        }

        var stamp = new Stamp(session, TurnId.New());
        live = stamp;
        tokenIssued = false;
        modelReported = false;

        return (stamp.Turn, new Reaction([new TurnStarted(session, stamp.Turn)], [Messages.User(turn)]));
    }

    public Reaction Receive(JsonNode message) => message.TextOr("type", string.Empty) switch
    {
        "system" when message.TextOr("subtype", string.Empty) == "init" => Initialized(message),
        "control_request" => desk.Receive(message, live),
        "control_cancel_request" => desk.Cancel(message, live),
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
            interrupting = true;

            return (stamp.Turn, desk.Release(interrupted: true).Then(Reaction.Send(Messages.Interrupt(++interruptions))));
        });

    private Reaction During(string type, JsonNode message, Stamp stamp) => type switch
    {
        "stream_event" or "assistant" or "user" when stale == 0 => translator.Receive(message, stamp),
        "rate_limit_event" => Reaction.Of([.. Telemetry.Limits(message).Select(limit => new LimitReported(session, stamp.Turn, limit))]),
        "result" => queued > 0 && !interrupting ? Carried(message, stamp) : Ended(message, stamp),
        _ => Reaction.None,
    };

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

    private Reaction Carried(JsonNode result, Stamp stamp)
    {
        queued--;

        return translator.Close(stamp, interrupted: false).Then(Reaction.Of(new UsageReported(session, stamp.Turn, Telemetry.Tokens(result), Spent(result))));
    }

    private Reaction Swallowed()
    {
        stale--;

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
        stale = interrupting ? queued : 0;
        queued = 0;
        var outcome = interrupting
            ? TurnOutcome.Interrupted
            : result.TextOr("subtype", string.Empty) == "success" && !result.Flag("is_error") ? TurnOutcome.Finished : TurnOutcome.Failed;
        var closing = desk.Release(interrupting).Then(translator.Close(stamp, interrupting));
        live = Option<Stamp>.None;
        interrupting = false;

        return closing
            .Then(Reaction.Of(new UsageReported(session, stamp.Turn, Telemetry.Tokens(result), cost)))
            .Then(Token(stamp))
            .Then(Reaction.Of(new TurnCompleted(session, stamp.Turn, outcome)));
    }

    private Reaction Token(Stamp stamp) =>
        conversation.Match(
            id => Reaction.Of(new ResumeTokenIssued(session, stamp.Turn, new ConversationMark(id, spent).Token)),
            () => Reaction.None);
}
