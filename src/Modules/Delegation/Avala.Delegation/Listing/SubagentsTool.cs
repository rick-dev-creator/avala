using System.Text.Json;
using Avala.Agents.Contracts.Sessions;

namespace Avala.Delegation.Listing;

internal static class SubagentsTool
{
    public const string Name = "subagents";

    public static HarnessTool Definition { get; } = new(
        Name,
        "List every sub-agent of your job with its real state, as Avala knows it: the ones your harness ran inside your session, including those of earlier sessions of this job lost when it ended, and the ones you delegated to another harness with delegate. Each has its name, its kind, inProcess or crossHarness, its state, working, waiting, completed, failed, stopped or lost, and its status, which says what a waiting one waits for; a crossHarness one also names its job, connection and harness.",
        """
        {
          "type": "object",
          "properties": {},
          "additionalProperties": false
        }
        """,
        ToolSurface.Executed);

    public static bool Accepts(string input)
    {
        try
        {
            using var document = JsonDocument.Parse(input.Length == 0 ? "{}" : input);

            return document.RootElement.ValueKind == JsonValueKind.Object && !document.RootElement.EnumerateObject().Any();
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
