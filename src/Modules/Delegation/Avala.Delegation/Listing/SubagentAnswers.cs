using System.Text.Json.Nodes;
using Avala.Agents.Contracts.Sessions;
using Avala.Delegation.Contracts;

namespace Avala.Delegation.Listing;

internal enum ListingRefusal
{
    MalformedInput,
    NoJob,
}

internal static class SubagentAnswers
{
    public static ToolResult Listed(ItemId item, IReadOnlyList<ListedSubagent> subagents) =>
        new(item, new JsonObject { ["subagents"] = new JsonArray([.. subagents.Select(Entry)]) }.ToJsonString());

    public static ToolResult Refused(ItemId item, ListingRefusal refusal) =>
        new(item, new JsonObject
        {
            ["refused"] = Camel(refusal.ToString()),
            ["reason"] = refusal == ListingRefusal.NoJob
                ? "this session runs no job, so Avala knows no sub-agents of it."
                : "the input takes no fields.",
        }.ToJsonString()) { IsError = true };

    private static JsonNode? Entry(ListedSubagent subagent)
    {
        var entry = new JsonObject
        {
            ["name"] = subagent.Name,
            ["kind"] = Camel(subagent.Kind.ToString()),
            ["state"] = Camel(subagent.State.ToString()),
            ["status"] = subagent.Status,
        };

        foreach (var job in subagent.Job.Match<string[]>(found => [found.Value.ToString()], () => []))
        {
            entry["job"] = job;
            entry["connection"] = subagent.Connection.Match(name => name.Value, () => string.Empty);
            entry["harness"] = subagent.Harness.Match(name => name, () => string.Empty);
            entry["role"] = subagent.Role.Match(role => Camel(role.ToString()), () => Camel(nameof(ChildRole.Worker)));
        }

        return entry;
    }

    private static string Camel(string name) => $"{char.ToLowerInvariant(name[0])}{name[1..]}";
}
