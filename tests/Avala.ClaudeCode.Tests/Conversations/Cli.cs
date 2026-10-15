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

    public static JsonNode Nested(JsonNode message, string parent)
    {
        message["parent_tool_use_id"] = parent;

        return message;
    }

    public static IEnumerable<JsonNode> DraftedTool(string message, int index, string id, string name, params string[] partials)
    {
        yield return Parse($$"""{ "type": "stream_event", "event": { "type": "message_start", "message": { "id": "{{message}}" } }, "parent_tool_use_id": null }""");
        yield return Parse($$"""{ "type": "stream_event", "event": { "type": "content_block_start", "index": {{index}}, "content_block": { "type": "tool_use", "id": "{{id}}", "name": "{{name}}", "input": {} } }, "parent_tool_use_id": null }""");

        foreach (var partial in partials)
        {
            yield return new JsonObject
            {
                ["type"] = "stream_event",
                ["event"] = new JsonObject
                {
                    ["type"] = "content_block_delta",
                    ["index"] = (long)index,
                    ["delta"] = new JsonObject { ["type"] = "input_json_delta", ["partial_json"] = partial },
                },
                ["parent_tool_use_id"] = null,
            };
        }
    }

    public static JsonNode Stopped(int index) =>
        Parse($$"""{ "type": "stream_event", "event": { "type": "content_block_stop", "index": {{index}} }, "parent_tool_use_id": null }""");

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

    public static JsonNode Lifecycle(string command, string state) =>
        Parse($$"""{ "type": "command_lifecycle", "command_uuid": "{{command}}", "state": "{{state}}", "uuid": "{{Guid.NewGuid()}}", "session_id": "{{Session}}" }""");

    public static JsonNode Bridge(string state) =>
        Parse($$"""{ "type": "system", "subtype": "bridge_state", "state": "{{state}}", "uuid": "{{Guid.NewGuid()}}", "session_id": "{{Session}}" }""");

    public static JsonNode RemoteAnswer(string requestId, string link) =>
        Parse($$"""
            { "type": "control_response", "response": { "subtype": "success", "request_id": "{{requestId}}",
              "response": { "session_url": "{{link}}", "connect_url": "https://claude.ai/code?environment=", "environment_id": "", "bridge_epoch": 1 } } }
            """);

    public static JsonNode RemoteRefusal(string requestId, string error) =>
        Parse($$"""{ "type": "control_response", "response": { "subtype": "error", "request_id": "{{requestId}}", "error": "{{error}}" } }""");

    public static JsonNode Replayed(string text, bool fromDevice, string uuid = "") =>
        Parse($$"""
            { "type": "user", "isReplay": true, {{(fromDevice ? "\"origin\": { \"kind\": \"human\" }," : string.Empty)}}
              "message": { "role": "user", "content": "{{text}}" }, "parent_tool_use_id": null, "session_id": "{{Session}}", "uuid": "{{(uuid.Length > 0 ? uuid : Guid.NewGuid().ToString())}}" }
            """);

    public static JsonNode RateLimit() =>
        Parse("""
            { "type": "rate_limit_event", "rate_limit_info": { "status": "allowed_warning", "resetsAt": 1791691200, "rateLimitType": "seven_day", "utilization": 0.99,
              "unifiedWindows": { "five_hour": { "utilization": 0, "resetsAt": 1791580200 }, "seven_day": { "utilization": 0.99, "resetsAt": 1791691200 } } } }
            """);

    public static JsonNode RejectedRateLimit() =>
        Parse("""
            { "type": "rate_limit_event", "rate_limit_info": { "status": "rejected", "resetsAt": 1791580200, "rateLimitType": "five_hour", "utilization": 1,
              "unifiedWindows": { "five_hour": { "utilization": 1, "resetsAt": 1791580200 }, "seven_day": { "utilization": 0.4, "resetsAt": 1791691200 } } } }
            """);

    public static JsonNode[] RefreshFailed() =>
    [
        Parse($$"""{ "type": "system", "subtype": "status", "status": "requesting", "session_id": "{{Session}}", "uuid": "00000000-0000-0000-0000-000000000001" }"""),
        Parse($$"""
            { "type": "assistant", "message": { "id": "00000000-0000-0000-0000-000000000002", "model": "<synthetic>", "role": "assistant", "stop_reason": "stop_sequence", "stop_sequence": "", "type": "message",
              "usage": { "input_tokens": 0, "output_tokens": 0, "cache_creation_input_tokens": 0, "cache_read_input_tokens": 0 },
              "content": [ { "type": "text", "text": "{{RefreshFailure}}" } ] },
              "parent_tool_use_id": null, "session_id": "{{Session}}", "error": "server_error", "is_api_error_message": true }
            """),
        Parse($$"""
            { "type": "result", "subtype": "success", "is_error": true, "api_error_status": null, "terminal_reason": "api_error", "stop_reason": "stop_sequence", "num_turns": 1,
              "duration_ms": 7209, "duration_api_ms": 0, "total_cost_usd": 0, "session_id": "{{Session}}", "result": "{{RefreshFailure}}",
              "usage": { "input_tokens": 0, "cache_creation_input_tokens": 0, "cache_read_input_tokens": 0, "output_tokens": 0, "output_tokens_details": { "thinking_tokens": 0 } },
              "modelUsage": {}, "permission_denials": [] }
            """),
    ];

    public static JsonNode[] WeekExhausted() =>
    [
        Parse($$"""{ "type": "system", "subtype": "status", "status": "requesting", "session_id": "{{Session}}", "uuid": "00000000-0000-0000-0000-000000000011" }"""),
        Parse($$"""
            { "type": "rate_limit_event", "rate_limit_info": { "status": "rejected", "resetsAt": 1791961200, "rateLimitType": "seven_day", "overageStatus": "rejected", "overageDisabledReason": "out_of_credits", "isUsingOverage": false,
              "unifiedWindows": { "five_hour": { "utilization": 0, "resetsAt": 1791732000 }, "seven_day": { "utilization": 1, "resetsAt": 1791961200 } } },
              "uuid": "00000000-0000-0000-0000-000000000012", "session_id": "{{Session}}" }
            """),
        Parse($$"""
            { "type": "assistant", "message": { "diagnostics": null, "id": "00000000-0000-0000-0000-000000000013", "container": null, "model": "<synthetic>", "role": "assistant", "stop_details": null, "stop_reason": "stop_sequence", "stop_sequence": "", "type": "message",
              "usage": { "output_tokens_details": null, "input_tokens": 0, "output_tokens": 0, "cache_creation_input_tokens": 0, "cache_read_input_tokens": 0 },
              "content": [ { "type": "text", "text": "{{WeekExhaustion}}" } ], "context_management": null },
              "parent_tool_use_id": null, "session_id": "{{Session}}", "uuid": "00000000-0000-0000-0000-000000000014", "timestamp": "2026-10-11T10:29:40.550Z",
              "error": "rate_limit", "is_api_error_message": true, "api_error": "usage_limit_reached",
              "api_error_params": { "rate_limit_info": { "status": "rejected", "resetsAt": 1791961200, "rateLimitType": "seven_day", "overageStatus": "rejected", "overageDisabledReason": "out_of_credits", "isUsingOverage": false } } }
            """),
        Parse($$"""
            { "duration_api_ms": 0, "stop_reason": "stop_sequence", "session_id": "{{Session}}", "total_cost_usd": 0,
              "usage": { "output_tokens_details": { "thinking_tokens": 0 }, "input_tokens": 0, "cache_creation_input_tokens": 0, "cache_read_input_tokens": 0, "output_tokens": 0 },
              "modelUsage": {}, "permission_denials": [], "terminal_reason": "api_error", "is_error": true, "num_turns": 1, "subtype": "success",
              "api_error_status": 429, "api_error": "usage_limit_reached", "result": "{{WeekExhaustion}}", "type": "result", "duration_ms": 1958,
              "uuid": "00000000-0000-0000-0000-000000000015", "queued_turn_count": 0, "result_index": 0 }
            """),
    ];

    public const string WeekExhaustion = "You've hit your weekly limit · resets Oct 14, 4pm (Asia/Tokyo)";

    public const string RefreshFailure ="Failed to refresh OAuth token: another Claude Code process is refreshing it or exited mid-refresh. This is usually transient; retry in a minute, and if it persists close other Claude Code processes or sign in again";

    public static JsonNode Parse(string json) => JsonNode.Parse(json)!;

    public static string OnHost(string input) =>
        new JsonObject(Parse(input).AsObject().Select(member => KeyValuePair.Create(
            member.Key,
            member.Value is JsonValue value && value.TryGetValue<string>(out var text) ? JsonValue.Create(HostPaths.Rooted(text)) : member.Value?.DeepClone()))).ToJsonString();
}
