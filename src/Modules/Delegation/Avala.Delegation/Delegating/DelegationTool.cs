using Avala.Agents.Contracts.Sessions;

namespace Avala.Delegation.Delegating;

internal static class DelegationTool
{
    public const string Name = "delegate";

    public static HarnessTool Definition { get; } = new(
        Name,
        "Delegate a self-contained piece of work to a sub-agent. It runs as a job of its own, in a worktree started from your current work, on a connection the harness chooses, governed and verified like any job. When it ends, its verified work is brought into your worktree and you receive its summary, the files it changed and its verification evidence. Several calls at once run in parallel.",
        """
        {
          "type": "object",
          "properties": {
            "instruction": { "type": "string", "description": "What the sub-agent must do, complete enough to be done on its own." },
            "autonomy": { "type": "string", "enum": ["supervised", "autonomous"], "description": "Optionally run the sub-agent stricter than you run; it can never run looser." },
            "role": { "type": "string", "enum": ["worker", "reviewer", "research"], "description": "worker, the default, changes files and its work is brought into yours; reviewer and research only read, cannot edit any file, and report back without changing your worktree." }
          },
          "required": ["instruction"],
          "additionalProperties": false
        }
        """,
        ToolSurface.Executed);
}
