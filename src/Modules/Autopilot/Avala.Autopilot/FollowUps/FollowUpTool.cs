using Avala.Agents.Contracts.Sessions;

namespace Avala.Autopilot.FollowUps;

internal static class FollowUpTool
{
    public const string Name = "propose_follow_up";

    public static HarnessTool Definition { get; } = new(
        Name,
        "Propose a follow-up task for the harness to run after this job, as a new job with its own review. The harness accepts or refuses it by its policy and tells you which.",
        """
        {
          "type": "object",
          "properties": {
            "instruction": { "type": "string", "description": "The instruction of the follow-up job, complete enough to be done on its own." },
            "reason": { "type": "string", "description": "Why the follow-up is needed." }
          },
          "required": ["instruction"],
          "additionalProperties": false
        }
        """,
        ToolSurface.Executed);
}
