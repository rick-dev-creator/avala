using System.Text.Json.Nodes;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.ClaudeCode.Conversations;
using Avala.ClaudeCode.Protocol;
using Avala.Sdk;

namespace Avala.ClaudeCode.Tests.Conversations;

internal sealed class Talk
{
    public const string WorkingDirectory = "/work";

    public const string Plans = "/home/ana/.claude-work/plans";

    public Talk(PermissionMode mode = PermissionMode.AskEveryTime, IReadOnlyList<HarnessTool>? tools = null, Option<ConversationMark> resumed = default, string plans = Plans) =>
        Conversation = new Conversation(Session, new SessionOptions(WorkingDirectory, mode) { Tools = tools ?? [] }, new Places(WorkingDirectory, plans), resumed);

    public SessionId Session { get; } = SessionId.New();

    public Conversation Conversation { get; }

    public List<IAgentEvent> Events { get; } = [];

    public List<JsonNode> Sent { get; } = [];

    public TurnId Turn { get; private set; }

    public Talk Begin(string text = "go")
    {
        var begun = Assert.IsType<(TurnId Turn, Reaction Reaction)>(Outcome(Conversation.Begin(new UserTurn(text))));
        Turn = begun.Turn;

        return Take(begun.Reaction);
    }

    public Talk Receive(params IEnumerable<JsonNode> messages)
    {
        foreach (var message in messages)
        {
            Take(Conversation.Receive(message));
        }

        return this;
    }

    public Talk Take(Result<Reaction, AgentError> result) => Take(Assert.IsType<Reaction>(Outcome(result)));

    public Talk Take(Reaction reaction)
    {
        Events.AddRange(reaction.Events);
        Sent.AddRange(reaction.Outgoing);

        return this;
    }

    public List<IAgentEvent> Drain()
    {
        var drained = Events.ToList();
        Events.Clear();
        Sent.Clear();

        return drained;
    }

    public JsonNode McpResult(string requestId) =>
        Sent.Last(message => (string?)message["response"]?["request_id"] == requestId)["response"]!["response"]!["mcp_response"]!["result"]!;

    public JsonNode Decision(string requestId) => JsonNode.Parse((string)McpResult(requestId)["content"]![0]!["text"]!)!;

    public JsonNode HookAnswer(string requestId) =>
        Sent.Last(message => (string?)message["response"]?["request_id"] == requestId)["response"]!["response"]!;

    private static object Outcome<T>(Result<T, AgentError> result) =>
        result.Match<object>(value => value!, error => error);
}
