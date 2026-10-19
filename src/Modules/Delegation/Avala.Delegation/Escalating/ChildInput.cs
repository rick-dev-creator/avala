using System.Text.Json;
using System.Text.Json.Nodes;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Delegation.Escalating;

internal static class ChildInput
{
    public static Option<JobId> Parse(string input)
    {
        try
        {
            using var document = JsonDocument.Parse(input);
            var root = document.RootElement;

            return root.ValueKind == JsonValueKind.Object
                && root.EnumerateObject().All(field => field.Name == "child")
                && root.TryGetProperty("child", out var child)
                && child.ValueKind == JsonValueKind.String
                && Guid.TryParse(child.GetString(), out var job)
                    ? new JobId(job)
                    : Option<JobId>.None;
        }
        catch (JsonException)
        {
            return Option<JobId>.None;
        }
    }

    public static ToolResult NotYourChild(ItemId item) =>
        new(item, new JsonObject { ["refused"] = "notYourChild", ["reason"] = "the input needs child, the job of a sub-agent you delegated to, as the early result named it." }.ToJsonString()) { IsError = true };
}
