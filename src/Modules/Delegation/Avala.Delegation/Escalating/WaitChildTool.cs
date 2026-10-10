using Avala.Agents.Contracts.Sessions;

namespace Avala.Delegation.Escalating;

internal static class WaitChildTool
{
    public const string Name = "wait_child";

    public static HarnessTool Definition { get; } = new(
        Name,
        "Keep waiting for a sub-agent you delegated to, after its delegate call returned early because it asked you something. The call is answered with the sub-agent's report when it ends, as its delegate call would have been, or earlier again if it asks you something else.",
        """
        {
          "type": "object",
          "properties": {
            "child": { "type": "string", "description": "The sub-agent's job, as the early result named it." }
          },
          "required": ["child"],
          "additionalProperties": false
        }
        """,
        ToolSurface.Executed);
}
