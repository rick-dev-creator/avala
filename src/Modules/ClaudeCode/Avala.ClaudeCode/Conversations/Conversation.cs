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
    private Option<Stamp> live;
    private Option<Guid> conversation;
    private decimal spent;
    private bool tokenIssued;
    private bool interrupting;
    private int interruptions;

    public Conversation(SessionId session, SessionOptions options, string workingDirectory, Option<ConversationMark> resumed)
    {
        var tools = new ToolBook(options.Tools);
        this.session = session;
        translator = new StreamTranslator(tools, workingDirectory);
        desk = new ControlDesk(tools, options.Permissions, workingDirectory);
        conversation = resumed.Map(mark => mark.Session);
        spent = resumed.Match(mark => mark.Spent, () => 0m);
    }

    public Option<TurnId> Live => live.Map(stamp => stamp.Turn);

    public Result<(TurnId Turn, Reaction Reaction), AgentError> Begin(UserTurn turn)
    {
        if (live.IsSome)
        {
            return AgentError.TurnInProgress;
        }

        var stamp = new Stamp(session, TurnId.New());
        live = stamp;
        tokenIssued = false;

        return (stamp.Turn, new Reaction([new TurnStarted(session, stamp.Turn)], [Messages.User(turn)]));
    }

    public Reaction Receive(JsonNode message) => message.TextOr("type", string.Empty) switch
    {
        "system" when message.TextOr("subtype", string.Empty) == "init" => Initialized(message),
        "stream_event" or "assistant" or "user" => live.Match(stamp => translator.Receive(message, stamp), () => Reaction.None),
        "rate_limit_event" => live.Match(
            stamp => Reaction.Of([.. Telemetry.Limits(message).Select(limit => new LimitReported(session, stamp.Turn, limit))]),
            () => Reaction.None),
        "result" => live.Match(stamp => Ended(message, stamp), () => Reaction.None),
        "control_request" => desk.Receive(message, live),
        "control_cancel_request" => desk.Cancel(message),
        _ => Reaction.None,
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

    private Reaction Initialized(JsonNode message)
    {
        var known = Guid.TryParse(message.TextOr("session_id", string.Empty), out var id) ? id : Option<Guid>.None;
        var changed = known.IsSome && known != conversation;
        conversation = known.IsSome ? known : conversation;

        return live.Match(
            stamp =>
            {
                if (tokenIssued && !changed)
                {
                    return Reaction.None;
                }

                tokenIssued = conversation.IsSome;

                return Token(stamp);
            },
            () => Reaction.None);
    }

    private Reaction Ended(JsonNode result, Stamp stamp)
    {
        var total = Telemetry.TotalCost(result);
        var cost = total.Map(amount => new Cost(Math.Max(0m, amount - spent), "USD"));
        spent = total.Match(amount => amount, () => spent);
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
