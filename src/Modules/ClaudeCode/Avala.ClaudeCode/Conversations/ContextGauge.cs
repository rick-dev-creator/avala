using System.Text.Json.Nodes;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.ClaudeCode.Protocol;

namespace Avala.ClaudeCode.Conversations;

internal sealed class ContextGauge
{
    private long used;
    private string model = string.Empty;
    private bool compacted;

    public void Begin()
    {
        used = 0;
        compacted = false;
    }

    public void Saw(JsonNode assistant)
    {
        if (assistant.Text("parent_tool_use_id").IsSome)
        {
            return;
        }

        var message = assistant.Members("message");
        var usage = message.Members("usage");
        var total = usage.Number("input_tokens") + usage.Number("cache_read_input_tokens") + usage.Number("cache_creation_input_tokens");

        if (total > 0)
        {
            used = total;
            model = message.TextOr("model", model);
        }
    }

    public void Compacted() => compacted = true;

    public Reaction Report(SessionId session, TurnId turn, JsonNode result)
    {
        var window = Window(result.Members("modelUsage"));

        return used > 0 && window > 0
            ? Reaction.Of(new ContextReported(session, turn, used, window) { Compacted = compacted })
            : Reaction.None;
    }

    private long Window(JsonObject models) =>
        models[model] is JsonObject ran
            ? ran.Number("contextWindow")
            : models.Select(entry => entry.Value is JsonObject other ? other.Number("contextWindow") : 0L).DefaultIfEmpty(0L).Max();
}
