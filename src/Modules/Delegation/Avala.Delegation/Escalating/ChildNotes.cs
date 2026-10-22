using System.Text.Json.Nodes;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Delegation.Contracts;
using Avala.Permissions.Contracts;

namespace Avala.Delegation.Escalating;

internal static class ChildNotes
{
    private const int LongestTitle = 80;

    private const string HowToAnswer = "Answer with the answer_child tool: allow with one answer per field to answer it, or deny to decline it, with an optional message saying what to do instead. Its question comes to you, never to a person: if a person should decide, ask them yourself, then answer.";

    public static string Asking(FormDecision decision) => $"asks: {decision.Form.Title}";

    public static string Form(DelegationRecord child, FormDecision decision) =>
        string.Join(
            '\n',
            $"Sub-agent \"{Title(child)}\" is waiting for you: it {Asking(decision)}",
            $"It is job {Job(child)}, from your delegate call {child.Item.Value}. {HowToAnswer}",
            $"fields {Fields(decision.Form)}",
            $"answer_child {Example(child, decision.Item)}");

    public static ToolResult FormResult(DelegationRecord child, FormDecision decision) =>
        new(child.Item, new JsonObject
        {
            ["job"] = Job(child),
            ["outcome"] = "asking",
            ["status"] = "running",
            ["state"] = "waiting",
            ["kind"] = "crossHarness",
            ["asks"] = Asking(decision),
            ["note"] = $"Your sub-agent \"{Title(child)}\" is still running and waits for your answer before it can go on. {HowToAnswer} Then call wait_child with this child to keep waiting for its report, or stop_child to stop it instead.",
            ["fields"] = JsonNode.Parse(Fields(decision.Form)),
            ["answer_child"] = new JsonObject { ["child"] = Job(child), ["request"] = decision.Item.Value, ["decision"] = "allow" },
            ["wait_child"] = new JsonObject { ["child"] = Job(child) },
            ["stop_child"] = new JsonObject { ["child"] = Job(child) },
        }.ToJsonString());

    public static string Title(DelegationRecord child)
    {
        var line = child.Instruction.Split('\n', 2)[0].Trim();

        return line.Length <= LongestTitle ? line : string.Concat(line.AsSpan(0, LongestTitle - 1), "…");
    }

    private static string Example(DelegationRecord child, ItemId request) =>
        new JsonObject { ["child"] = Job(child), ["request"] = request.Value, ["decision"] = "allow" }.ToJsonString();

    private static string Fields(AgentForm form) =>
        new JsonArray([.. form.Fields.Select(field => (JsonNode?)new JsonObject
        {
            ["id"] = field.Id,
            ["prompt"] = field.Prompt,
            ["kind"] = field.Kind.ToString(),
            ["options"] = new JsonArray([.. field.Options.Select(option => (JsonNode?)JsonValue.Create(option.Label))]),
            ["acceptsText"] = field.AcceptsFreeText,
        })]).ToJsonString();

    private static string Job(DelegationRecord child) => child.Child.Match(job => job.Value.ToString(), () => string.Empty);
}
