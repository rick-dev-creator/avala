using System.Text.Json.Nodes;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;

namespace Avala.ClaudeCode.Conversations;

internal static class Messages
{
    public const string GateHook = "avala-gate";

    public static JsonNode User(UserTurn turn) => new JsonObject
    {
        ["type"] = "user",
        ["message"] = new JsonObject { ["role"] = "user", ["content"] = turn.Text },
        ["parent_tool_use_id"] = null,
        ["session_id"] = string.Empty,
    };

    public static JsonNode Initialize() => Request("avala-initialize", new JsonObject
    {
        ["subtype"] = "initialize",
        ["hooks"] = new JsonObject
        {
            ["PreToolUse"] = new JsonArray(new JsonObject { ["matcher"] = "*", ["hookCallbackIds"] = new JsonArray(GateHook) }),
        },
    });

    public static JsonNode Interrupt(int number) => Request($"avala-interrupt-{number}", new JsonObject { ["subtype"] = "interrupt" });

    public static JsonNode Success(string requestId, JsonNode response) => new JsonObject
    {
        ["type"] = "control_response",
        ["response"] = new JsonObject { ["subtype"] = "success", ["request_id"] = requestId, ["response"] = response },
    };

    public static JsonNode Failure(string requestId, string error) => new JsonObject
    {
        ["type"] = "control_response",
        ["response"] = new JsonObject { ["subtype"] = "error", ["request_id"] = requestId, ["error"] = error },
    };

    public static JsonNode Mcp(string requestId, Option<JsonNode> rpcId, JsonNode result) =>
        Success(requestId, new JsonObject
        {
            ["mcp_response"] = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = Identifier(rpcId), ["result"] = result },
        });

    public static JsonNode McpError(string requestId, Option<JsonNode> rpcId, string error) =>
        Success(requestId, new JsonObject
        {
            ["mcp_response"] = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = Identifier(rpcId),
                ["error"] = new JsonObject { ["code"] = -32601, ["message"] = error },
            },
        });

    public static JsonNode ToolText(string text, bool isError = false) => new JsonObject
    {
        ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
        ["isError"] = isError,
    };

    public static JsonNode Allow(JsonObject input) =>
        ToolText(new JsonObject { ["behavior"] = "allow", ["updatedInput"] = input.DeepClone() }.ToJsonString());

    public static JsonNode Deny(string message, bool interrupt = false) =>
        ToolText(new JsonObject { ["behavior"] = "deny", ["message"] = message, ["interrupt"] = interrupt }.ToJsonString());

    public static JsonNode Ask() => new JsonObject
    {
        ["hookSpecificOutput"] = new JsonObject
        {
            ["hookEventName"] = "PreToolUse",
            ["permissionDecision"] = "ask",
            ["permissionDecisionReason"] = "Avala's permission policy decides every action.",
        },
    };

    private static JsonNode? Identifier(Option<JsonNode> rpcId) => rpcId.Match<JsonNode?>(id => id.DeepClone(), () => null);

    private static JsonObject Request(string id, JsonObject request) => new()
    {
        ["type"] = "control_request",
        ["request_id"] = id,
        ["request"] = request,
    };
}
