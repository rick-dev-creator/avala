using System.Text.Json;
using System.Text.Json.Nodes;
using Avala.Agents.Contracts.Sessions;
using Avala.ClaudeCode.Protocol;
using Avala.Sdk;

namespace Avala.ClaudeCode.Conversations;

internal sealed record ToolCall(string RequestId, Option<JsonNode> RpcId, string Tool, JsonObject Arguments, string ToolUseId);

internal sealed class AvalaServer(IReadOnlyList<HarnessTool> offered)
{
    private const string LatestProtocol = "2025-06-18";

    private const string PermissionDescription =
        "Internal to Avala: answers the permission prompts of Claude Code with Avala's policy. Never call it yourself.";

    public Reaction Handle(string requestId, JsonNode rpc, Func<ToolCall, Reaction> call)
    {
        var id = rpc.Field("id");

        return rpc.TextOr("method", string.Empty) switch
        {
            "initialize" => Reaction.Send(Messages.Mcp(requestId, id, new JsonObject
            {
                ["protocolVersion"] = rpc.Members("params").TextOr("protocolVersion", LatestProtocol),
                ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                ["serverInfo"] = new JsonObject { ["name"] = CommandLine.Server, ["version"] = "1.0.0" },
            })),
            "tools/list" => Reaction.Send(Messages.Mcp(requestId, id, new JsonObject { ["tools"] = Tools() })),
            "tools/call" => call(Call(requestId, id, rpc.Members("params"))),
            var method when method.StartsWith("notifications/", StringComparison.Ordinal) || method == "ping" =>
                Reaction.Send(Messages.Mcp(requestId, id, new JsonObject())),
            var method => Reaction.Send(Messages.McpError(requestId, id, $"Avala's server has no method {method}.")),
        };
    }

    private static ToolCall Call(string requestId, Option<JsonNode> id, JsonObject parameters) =>
        new(
            requestId,
            id,
            parameters.TextOr("name", string.Empty),
            parameters.Members("arguments"),
            parameters.Members("_meta").TextOr("claudecode/toolUseId", requestId));

    private JsonArray Tools() =>
    [
        Tool(CommandLine.PermissionTool, PermissionDescription, """
            { "type": "object", "properties": { "tool_name": { "type": "string" }, "input": { "type": "object" }, "tool_use_id": { "type": "string" } } }
            """),
        .. offered.Select(tool => Tool(tool.Name, tool.Description, tool.InputSchema)),
    ];

    private static JsonObject Tool(string name, string description, string schema) => new()
    {
        ["name"] = name,
        ["description"] = description,
        ["inputSchema"] = Schema(schema),
    };

    private static JsonNode Schema(string schema)
    {
        try
        {
            return JsonNode.Parse(schema) ?? new JsonObject { ["type"] = "object" };
        }
        catch (JsonException)
        {
            return new JsonObject { ["type"] = "object" };
        }
    }
}
