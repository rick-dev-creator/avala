using System.Text.Json.Nodes;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.ClaudeCode.Protocol;
using Avala.Sdk;

namespace Avala.ClaudeCode.Conversations;

internal sealed class ControlDesk(ToolBook tools, PermissionMode mode, Places places)
{
    private const string Interrupted = "The harness interrupted the turn.";

    private readonly AvalaServer server = new(tools.Offered);
    private readonly List<Prompt> prompts = [];
    private readonly Dictionary<ItemId, ToolCall> calls = [];

    public Reaction Receive(JsonNode request, Option<Stamp> turn)
    {
        var requestId = request.TextOr("request_id", string.Empty);
        var body = request.Members("request");

        return body.TextOr("subtype", string.Empty) switch
        {
            "hook_callback" => Reaction.Send(Messages.Success(requestId, Gate(body.Members("input")))),
            "mcp_message" => server.Handle(requestId, body.Members("message"), call => Called(call, turn)),
            var subtype => Reaction.Send(Messages.Failure(requestId, $"Avala does not handle {subtype} requests.")),
        };
    }

    public Reaction Cancel(JsonNode cancel, Option<Stamp> turn)
    {
        var requestId = cancel.TextOr("request_id", string.Empty);
        var withdrawn = prompts.Where(prompt => prompt.Call.RequestId == requestId && prompt.Announced).ToList();
        prompts.RemoveAll(prompt => prompt.Call.RequestId == requestId);

        foreach (var call in calls.Where(call => call.Value.RequestId == requestId).Select(call => call.Key).ToList())
        {
            calls.Remove(call);
        }

        return turn.Match(stamp => Withdrawn(withdrawn, stamp).Then(Announce(stamp)), () => Reaction.None);
    }

    public Result<Reaction, AgentError> Respond(PermissionDecision decision, Stamp stamp)
    {
        if (!Active(decision.Item, form: false).TryGetValue(out var prompt, out var error))
        {
            return error;
        }

        var allowed = decision.Answer == PermissionAnswer.Allow;
        prompt.Tool.Refused = !allowed;
        prompts.RemoveAt(0);

        return Reaction.Send(Messages.Mcp(
                prompt.Call.RequestId,
                prompt.Call.RpcId,
                allowed ? Messages.Allow(prompt.Input) : Messages.Deny(decision.Message.Match(message => message, () => "The harness denied this action."))))
            .Then(Reaction.Of(new PermissionResolved(stamp.Session, stamp.Turn, decision.Item, decision.Answer)))
            .Then(Announce(stamp));
    }

    public Result<Reaction, AgentError> Answer(FormAnswer answer, Stamp stamp)
    {
        if (!Active(answer.Item, form: true).TryGetValue(out var prompt, out var error))
        {
            return error;
        }

        var tool = prompt.Tool.Use.Name;
        var approves = Questions.Approves(tool, answer);
        prompt.Tool.Refused = !approves;
        prompts.RemoveAt(0);

        return Reaction.Send(Messages.Mcp(
                prompt.Call.RequestId,
                prompt.Call.RpcId,
                approves ? Messages.Allow(Questions.Answered(tool, prompt.Input, answer)) : Messages.Deny(Questions.Refusal(tool, answer))))
            .Then(Reaction.Of(new FormAnswered(stamp.Session, stamp.Turn, answer.Item, answer)))
            .Then(Announce(stamp));
    }

    public Result<Reaction, AgentError> Return(ToolResult result, Stamp stamp)
    {
        if (!calls.Remove(result.Item, out var call))
        {
            return AgentError.NoPendingCall;
        }

        foreach (var tool in tools.Find(result.Item.Value).Match<TrackedTool[]>(tool => [tool], () => []))
        {
            tool.Closed = true;
        }

        return Reaction.Send(Messages.Mcp(call.RequestId, call.RpcId, Messages.ToolText(result.Content, result.IsError)))
            .Then(Reaction.Of(
                new ToolReturned(stamp.Session, stamp.Turn, result.Item, result),
                new ItemCompleted(stamp.Session, stamp.Turn, result.Item, result.IsError ? ItemOutcome.Failed : ItemOutcome.Succeeded)));
    }

    public Reaction Release(bool interrupted)
    {
        var released = prompts
            .Select(prompt =>
            {
                prompt.Tool.Refused = true;

                return Messages.Mcp(prompt.Call.RequestId, prompt.Call.RpcId, Messages.Deny(Interrupted, interrupted));
            })
            .Concat(calls.Values.Select(call => Messages.Mcp(call.RequestId, call.RpcId, Messages.ToolText(Interrupted, isError: true))))
            .ToList();
        prompts.Clear();
        calls.Clear();

        return Reaction.Send(released);
    }

    private Result<Prompt, AgentError> Active(ItemId item, bool form) =>
        prompts.Count > 0 && prompts[0].Announced && prompts[0].Tool.Item == item && prompts[0].Form.IsSome == form
            ? prompts[0]
            : form ? AgentError.NoPendingForm : AgentError.NoPendingPermission;

    private JsonNode Gate(JsonObject input)
    {
        var use = new ToolUse(input.TextOr("tool_use_id", string.Empty), input.TextOr("tool_name", string.Empty), input.Members("tool_input"));

        return !use.WritesPlan(places) && (use.Gated && CommandLine.Unqualified(use.Name).IsNone || use.ReadsOutside(places.WorkingDirectory)) ? Messages.Ask() : new JsonObject();
    }

    private string Plan() => tools.All.Select(tool => tool.Use).Where(use => use.WritesPlan(places)).Aggregate(string.Empty, (plan, use) => use.Planned(plan));

    private Reaction Called(ToolCall call, Option<Stamp> turn) =>
        turn.Match(
            stamp => call.Tool == CommandLine.PermissionTool ? Prompted(call, stamp) : Harness(call, stamp),
            () => Reaction.Send(Messages.Mcp(call.RequestId, call.RpcId, Messages.ToolText("No turn is running.", isError: true))));

    private Reaction Harness(ToolCall call, Stamp stamp)
    {
        var tool = tools.Track(new ToolUse(call.ToolUseId, CommandLine.Qualified(call.Tool), call.Arguments));

        switch (tool.Role)
        {
            case ToolRole.Canvas:
                var readable = call.Arguments.Text("title").IsSome && call.Arguments.Text("mediaType").IsSome && call.Arguments.Text("content").IsSome;
                var opening = tool.Opened
                    ? Reaction.None
                    : Reaction.Of(new CanvasStarted(stamp.Session, stamp.Turn, tool.Item, call.Arguments.TextOr("title", "Canvas"), call.Arguments.TextOr("mediaType", "text/plain")));
                tool.Opened = true;
                tool.Closed = true;

                return opening
                    .Then(Reaction.Send(Messages.Mcp(call.RequestId, call.RpcId, Messages.ToolText(readable ? "The canvas is drawn." : "The canvas needs a title, a mediaType and a content.", !readable))))
                    .Then(Reaction.Of(new ItemCompleted(stamp.Session, stamp.Turn, tool.Item, readable ? ItemOutcome.Succeeded : ItemOutcome.Failed)));
            case ToolRole.Executed when !tool.Opened:
                tool.Opened = true;
                calls[tool.Item] = call;

                return Reaction.Of(new ToolCalled(stamp.Session, stamp.Turn, tool.Item, call.Tool, call.Arguments.ToJsonString()));
            default:
                return Reaction.Send(Messages.Mcp(call.RequestId, call.RpcId, Messages.ToolText($"Avala offers no tool {call.Tool} to this session.", isError: true)));
        }
    }

    private Reaction Prompted(ToolCall call, Stamp stamp)
    {
        var name = call.Arguments.TextOr("tool_name", string.Empty);
        var input = call.Arguments.Members("input");
        var tool = tools.Track(new ToolUse(call.Arguments.TextOr("tool_use_id", call.ToolUseId), name, input));
        var own = CommandLine.Unqualified(name);

        if (own == Option<string>.Some(CommandLine.PermissionTool))
        {
            return Reaction.Send(Messages.Mcp(call.RequestId, call.RpcId, Messages.Deny("This tool is internal to Avala.")));
        }

        if (tool.Role is ToolRole.Canvas or ToolRole.Executed || tool.Role != ToolRole.Form && (Granted(tool.Use) || (tool.Use with { Input = input }).WritesPlan(places)))
        {
            return Reaction.Send(Messages.Mcp(call.RequestId, call.RpcId, Messages.Allow(input)));
        }

        prompts.Add(new Prompt(call, tool, input, tool.Role == ToolRole.Form ? Questions.Form(name, input, Plan()) : Option<AgentForm>.None));

        return Announce(stamp);
    }

    private bool Granted(ToolUse use) =>
        mode == PermissionMode.AllowAll || mode == PermissionMode.AllowEdits && use.Kind == ItemKind.FileEdit;

    private Reaction Announce(Stamp stamp)
    {
        if (prompts.Count == 0 || prompts[0].Announced)
        {
            return Reaction.None;
        }

        var prompt = prompts[0];
        prompt.Announced = true;
        var tool = prompt.Tool;

        if (prompt.Form.IsSome)
        {
            tool.Opened = true;

            return Reaction.Of(prompt.Form.Match<IAgentEvent[]>(form => [new FormRequested(stamp.Session, stamp.Turn, tool.Item, form)], () => []));
        }

        var use = tool.Use with { Input = prompt.Input };
        var opening = tool.Opened
            ? Reaction.None
            : Reaction.Of(new ItemStarted(stamp.Session, stamp.Turn, tool.Item, use.KindIn(places), use.Heading(places)) { Input = use.Details(places.WorkingDirectory) });
        tool.Opened = true;

        return opening.Then(Reaction.Of(new PermissionRequested(stamp.Session, stamp.Turn, tool.Item, use.Heading(places), use.Kind, use.Target(places.WorkingDirectory))));
    }

    private static Reaction Withdrawn(IEnumerable<Prompt> withdrawn, Stamp stamp) =>
        Reaction.Of([.. withdrawn.Select(prompt =>
        {
            prompt.Tool.Refused = true;

            return new RequestWithdrawn(stamp.Session, stamp.Turn, prompt.Tool.Item);
        })]);

    private sealed record Prompt(ToolCall Call, TrackedTool Tool, JsonObject Input, Option<AgentForm> Form)
    {
        public bool Announced { get; set; }
    }
}
