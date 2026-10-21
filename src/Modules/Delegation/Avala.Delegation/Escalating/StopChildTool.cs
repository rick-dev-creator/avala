using Avala.Agents.Contracts.Sessions;

namespace Avala.Delegation.Escalating;

internal static class StopChildTool
{
    public const string Name = "stop_child";

    public static HarnessTool Definition { get; } = new(
        Name,
        "Stop a sub-agent you delegated to, such as one whose work you no longer need. Its job is discarded and none of its work reaches yours; the call is answered with its report, whose state is stopped.",
        """
        {
          "type": "object",
          "properties": {
            "child": { "type": "string", "description": "The sub-agent's job, as its delegate call or the subagents tool named it." }
          },
          "required": ["child"],
          "additionalProperties": false
        }
        """,
        ToolSurface.Executed);
}
