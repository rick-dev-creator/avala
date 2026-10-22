using Avala.Agents.Contracts.Sessions;

namespace Avala.Delegation.Escalating;

internal static class AnswerChildTool
{
    public const string Name = "answer_child";

    public static HarnessTool Definition { get; } = new(
        Name,
        "Answer a question one of your sub-agents is waiting on, which the harness told you about. allow answers it with one answer per field; deny declines it, with an optional message telling the sub-agent what to do instead. A sub-agent's question comes to you and never to a person: if a person should decide, ask them yourself, then answer.",
        """
        {
          "type": "object",
          "properties": {
            "child": { "type": "string", "description": "The sub-agent's job, as the message names it." },
            "request": { "type": "string", "description": "The request, as the message names it." },
            "decision": { "type": "string", "enum": ["allow", "deny"] },
            "message": { "type": "string", "description": "Optionally, what the sub-agent is told with your answer." },
            "fields": {
              "type": "array",
              "description": "One answer per field of the question.",
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
