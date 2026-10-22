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
    private readonly ContextGauge context;
    private readonly RemoteBridge remote;
    private readonly Option<string> effort;
    private Option<Stamp> live;
    private Option<Guid> conversation;
    private decimal spent;
    private bool tokenIssued;
    private bool modelReported;
    private bool interrupting;
    private bool carried;
    private bool woken;
    private bool acknowledging;
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
        context = new ContextGauge(CommandLine.CompactsAt(options));
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
            carried = carried && live.IsNone;

            return live.ToResult(AgentError.NoTurnInProgress).Map(running =>
                (running.Turn, new Reaction([new MessageQueued(session, running.Turn, turn.Text) { Attachments = turn.Attachments, Author = turn.Author }], [Messages.User(turn, commands.Send(midTurn: true), attachments)])));
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
        "system" => FromSystem(message),
        "control_request" => Requested(message),
        "control_cancel_request" => desk.Cancel(message, live),
        "control_response" => Responded(message),
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
            var background = translator.WorkingInBackground && (carried || !commands.Owed);
            var generating = woken && !carried;

            if (!commands.Owed && !background && !generating && !acknowledging)
            {
                return (stamp.Turn, Closed(stamp, TurnOutcome.Interrupted, Reaction.None));
            }

            interrupting = true;
            acknowledging = acknowledging || background || generating;

            return (stamp.Turn, desk.Release(interrupted: true).Then(Reaction.Send(Messages.Interrupt(++interruptions))));
        });

    private Reaction FromSystem(JsonNode message) => message.TextOr("subtype", string.Empty) switch
    {
        "init" => Initialized(message),
        "bridge_state" => remote.Reported(message),
        _ => Continued("system", message),
    };

    private Reaction Responded(JsonNode response) =>
        RemoteBridge.Answers(response) ? remote.Answered(response)
        : acknowledging && Acknowledges(response) ? Acknowledged()
        : Continued("control_response", response);

    private static bool Acknowledges(JsonNode response) =>
        response["response"]?.TextOr("request_id", string.Empty).StartsWith(Messages.InterruptPrefix, StringComparison.Ordinal) == true;

    private Reaction Acknowledged()
    {
        acknowledging = false;

        return live.Match(stamp => Closed(stamp, TurnOutcome.Interrupted, Reaction.None), () => Reaction.None);
    }

    private Reaction Requested(JsonNode request)
    {
        var opening = ControlDesk.CallsTool(request) ? Woken() : Reaction.None;

        return opening.Then(desk.Receive(request, live));
    }

    private Reaction Continued(string type, JsonNode message)
    {
        var opening = type is "assistant" or "stream_event" || Notifies(type, message) || (type == "system" && translator.Restarts(message)) ? Woken() : Reaction.None;

        return opening.Then(live.Match(stamp => Dismissing(During(type, message, stamp), stamp), () => Reaction.None));
    }

    private Reaction Dismissing(Reaction reaction, Stamp stamp) =>
        reaction.Then(desk.Dismiss(reaction.Events.OfType<ItemCompleted>().Select(completed => completed.Item), stamp));

    private Reaction During(string type, JsonNode message, Stamp stamp) => type switch
    {
        "assistant" when stale == 0 => Said(message, stamp),
        "stream_event" or "user" when stale == 0 => translator.Receive(message, stamp),
        "system" when message.TextOr("subtype", string.Empty) == "compact_boundary" => Compacted(),
        "system" when message.TextOr("subtype", string.Empty) == "api_retry" => Retrying(message, stamp),
        "system" => translator.Tasked(message, stamp),
        "rate_limit_event" => Limited(message, stamp),
        "result" => !interrupting && (commands.Pending || (translator.WorkingInBackground && Outcome(message) != TurnOutcome.LimitReached)) ? Carried(message, stamp) : Ended(message, stamp),
        _ => Reaction.None,
    };

    private Reaction Said(JsonNode message, Stamp stamp)
    {
        context.Saw(message);

        return translator.Receive(message, stamp);
    }

    private Reaction Retrying(JsonNode message, Stamp stamp) =>
        Reaction.Of(new HarnessRetrying(
            session,
            stamp.Turn,
            (int)(message["attempt"]?.GetValue<double>() ?? 0),
            (int)(message["max_retries"]?.GetValue<double>() ?? 0),
            TimeSpan.FromMilliseconds(message["retry_delay_ms"]?.GetValue<double>() ?? 0),
            message.TextOr("error", message["error_status"]?.ToString() ?? string.Empty)));

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

    private Reaction Woken()
    {
        if (live.IsSome || stale != 0)
        {
            return Reaction.None;
        }

        var stamp = Opened();
        woken = true;

        return Reaction.Of(new TurnStarted(session, stamp.Turn) { Woken = true });
    }

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

    private bool Notifies(string type, JsonNode message) =>
        type == "system" && message.TextOr("subtype", string.Empty) == "task_notification" && translator.Wakes(message);

    private Reaction Carried(JsonNode result, Stamp stamp)
    {
        carried = true;

        if (commands.Pending)
        {
            commands.Answered();
        }

        return translator.Carry(stamp)
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
        acknowledging = false;
        carried = false;
        woken = false;

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
