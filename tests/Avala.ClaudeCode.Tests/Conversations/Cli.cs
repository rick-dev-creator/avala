using System.Text.Json.Nodes;
using Avala.Testing;

namespace Avala.ClaudeCode.Tests.Conversations;

internal static class Cli
{
    public const string Session = "4d81cdb1-40e2-4ee5-ab3f-4caf53d1bee0";

    public static JsonNode Init(string session = Session) =>
        Parse($$"""{ "type": "system", "subtype": "init", "session_id": "{{session}}", "cwd": "/work" }""");

    public static IEnumerable<JsonNode> Streamed(string message, int index, string blockType, params string[] deltas)
    {
        var deltaType = blockType == "thinking" ? "thinking_delta" : "text_delta";
        var field = blockType == "thinking" ? "thinking" : "text";

        yield return Parse($$"""{ "type": "stream_event", "event": { "type": "message_start", "message": { "id": "{{message}}" } } }""");
        yield return Parse($$"""{ "type": "stream_event", "event": { "type": "content_block_start", "index": {{index}}, "content_block": { "type": "{{blockType}}" } } }""");

        foreach (var delta in deltas)
        {
            yield return new JsonObject
            {
                ["type"] = "stream_event",
                ["event"] = new JsonObject
                {
                    ["type"] = "content_block_delta",
                    ["index"] = index,
                    ["delta"] = new JsonObject { ["type"] = deltaType, [field] = delta },
                },
            };
        }

        yield return Parse($$"""{ "type": "stream_event", "event": { "type": "content_block_stop", "index": {{index}} } }""");
    }

    public static JsonNode Said(string message, string blockType, string text) =>
        new JsonObject
        {
            ["type"] = "assistant",
            ["message"] = new JsonObject
            {
                ["id"] = message,
                ["content"] = new JsonArray(new JsonObject { ["type"] = blockType, [blockType == "thinking" ? "thinking" : "text"] = text }),
            },
        };

    public static JsonNode ToolUse(string id, string name, string input) =>
        new JsonObject
        {
            ["type"] = "assistant",
            ["message"] = new JsonObject
            {
                ["id"] = $"msg-{id}",
                ["content"] = new JsonArray(new JsonObject { ["type"] = "tool_use", ["id"] = id, ["name"] = name, ["input"] = Parse(input) }),
            },
        };

    public static JsonNode ToolResult(string id, string content, bool isError = false) =>
        new JsonObject
        {
            ["type"] = "user",
            ["message"] = new JsonObject
            {
                ["role"] = "user",
                ["content"] = new JsonArray(new JsonObject { ["type"] = "tool_result", ["tool_use_id"] = id, ["content"] = content, ["is_error"] = isError }),
            },
        };

    public static JsonNode Prompt(string requestId, string tool, string input, string toolUseId) =>
        McpCall(requestId, "permission_prompt", $$"""{ "tool_name": "{{tool}}", "input": {{input}}, "tool_use_id": "{{toolUseId}}" }""", toolUseId);

    public static JsonNode McpCall(string requestId, string tool, string arguments, string toolUseId) =>
        Mcp(requestId, $$"""{ "method": "tools/call", "params": { "name": "{{tool}}", "arguments": {{arguments}}, "_meta": { "claudecode/toolUseId": "{{toolUseId}}" } }, "jsonrpc": "2.0", "id": 7 }""");

    public static JsonNode Mcp(string requestId, string message) =>
        Parse($$"""{ "type": "control_request", "request_id": "{{requestId}}", "request": { "subtype": "mcp_message", "server_name": "avala", "message": {{message}} } }""");

    public static JsonNode Hook(string requestId, string tool, string input) =>
        Parse($$"""
            { "type": "control_request", "request_id": "{{requestId}}", "request": { "subtype": "hook_callback", "callback_id": "avala-gate",
              "input": { "hook_event_name": "PreToolUse", "tool_name": "{{tool}}", "tool_input": {{input}}, "tool_use_id": "t" } } }
            """);

    public static JsonNode Result(decimal totalCost, string subtype = "success", bool isError = false) =>
        Parse($$"""
            { "type": "result", "subtype": "{{subtype}}", "is_error": {{(isError ? "true" : "false")}}, "session_id": "{{Session}}",
              "total_cost_usd": {{totalCost.ToString(System.Globalization.CultureInfo.InvariantCulture)}},
              "usage": { "input_tokens": 4, "output_tokens": 200, "cache_read_input_tokens": 30611, "cache_creation_input_tokens": 6910,
                         "output_tokens_details": { "thinking_tokens": 12 } } }
            """);

    public static JsonNode RateLimit() =>
        Parse("""
            { "type": "rate_limit_event", "rate_limit_info": { "status": "allowed_warning", "resetsAt": 1791691200, "rateLimitType": "seven_day", "utilization": 0.99,
              "unifiedWindows": { "five_hour": { "utilization": 0, "resetsAt": 1791580200 }, "seven_day": { "utilization": 0.99, "resetsAt": 1791691200 } } } }
            """);

    public static JsonNode Parse(string json) => JsonNode.Parse(json)!;

    public static string OnHost(string input) =>
        new JsonObject(Parse(input).AsObject().Select(member => KeyValuePair.Create(
            member.Key,
            member.Value is JsonValue value && value.TryGetValue<string>(out var text) ? JsonValue.Create(HostPaths.Rooted(text)) : member.Value?.DeepClone()))).ToJsonString();
}
