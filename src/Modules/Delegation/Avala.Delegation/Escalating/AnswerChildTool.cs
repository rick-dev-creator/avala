using Avala.Agents.Contracts.Sessions;

namespace Avala.Delegation.Escalating;

internal static class AnswerChildTool
{
    public const string Name = "answer_child";

    public static HarnessTool Definition { get; } = new(
        Name,
        "Answer a request one of your sub-agents is waiting on, which the harness told you about in a message. allow grants it only if you would be allowed to do the same yourself, otherwise it goes to a person; deny refuses it, with an optional message telling the sub-agent what to do instead; person passes it to a person. For a question, allow answers it with fields and deny declines it.",
        """
        {
          "type": "object",
          "properties": {
            "child": { "type": "string", "description": "The sub-agent's job, as the message names it." },
            "request": { "type": "string", "description": "The request, as the message names it." },
            "decision": { "type": "string", "enum": ["allow", "deny", "person"] },
            "message": { "type": "string", "description": "Optionally, what the sub-agent is told with your answer." },
            "fields": {
              "type": "array",
              "description": "For a question: one answer per field.",
              "items": {
                "type": "object",
                "properties": {
                  "id": { "type": "string" },
                  "chosen": { "type": "array", "items": { "type": "string" } },
                  "text": { "type": "string" },
                  "confirmed": { "type": "boolean" }
                },
                "required": ["id"],
                "additionalProperties": false
              }
            }
          },
          "required": ["child", "request", "decision"],
          "additionalProperties": false
        }
        """,
        ToolSurface.Executed);
}
