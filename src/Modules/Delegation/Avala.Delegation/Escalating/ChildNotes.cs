using System.Globalization;
using System.Text.Json.Nodes;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Delegation.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;

namespace Avala.Delegation.Escalating;

internal static class ChildNotes
{
    private const int LongestTitle = 80;

    public static string Asking(PolicyDecision decision) => $"wants to {Wants(decision.Kind)}: {decision.Target}";

    public static string Asking(FormDecision decision) => $"asks: {decision.Form.Title}";

    public static string Permission(DelegationRecord child, PolicyDecision decision, ParentEscalation escalation) =>
        Note(child, decision.Item, $"it {Asking(decision)}", escalation, []);

    public static string Form(DelegationRecord child, FormDecision decision, ParentEscalation escalation) =>
        Note(
            child,
            decision.Item,
            $"it {Asking(decision)}",
            escalation,
            [$"fields {Fields(decision.Form)}", decision.Form.Purpose == FormPurpose.Permission ? "This asks a permission you cannot see, so you can only deny it or pass it to a person." : string.Empty]);

    public static ToolResult PermissionResult(DelegationRecord child, PolicyDecision decision, ParentEscalation escalation) =>
        Result(child, decision.Item, Asking(decision), escalation, Option<AgentForm>.None);

    public static ToolResult FormResult(DelegationRecord child, FormDecision decision, ParentEscalation escalation) =>
        Result(child, decision.Item, Asking(decision), escalation, decision.Form);

    private static ToolResult Result(DelegationRecord child, ItemId request, string asking, ParentEscalation escalation, Option<AgentForm> form)
    {
        var result = new JsonObject
        {
            ["job"] = Job(child),
            ["outcome"] = "asking",
            ["status"] = "running",
            ["asks"] = asking,
            ["note"] = $"Your sub-agent \"{Title(child)}\" is still running and waits for your decision before it can go on. Answer with answer_child: allow it only if you would be allowed to do it yourself, deny it with an optional message saying what to do instead, or pass it to a person. Then call wait_child with this child to keep waiting for its report. Without your answer within {Seconds(escalation.Window)} seconds it goes to a person.",
            ["answer_child"] = new JsonObject { ["child"] = Job(child), ["request"] = request.Value, ["decision"] = "allow" },
            ["wait_child"] = new JsonObject { ["child"] = Job(child) },
        };

        foreach (var asked in form.Match<AgentForm[]>(found => [found], () => []))
        {
            result["fields"] = JsonNode.Parse(Fields(asked));
            result["permissionForm"] = asked.Purpose == FormPurpose.Permission;
        }

        return new ToolResult(child.Item, result.ToJsonString());
    }

    public static string Title(DelegationRecord child)
    {
        var line = child.Instruction.Split('\n', 2)[0].Trim();

        return line.Length <= LongestTitle ? line : string.Concat(line.AsSpan(0, LongestTitle - 1), "…");
    }

    private static string Note(DelegationRecord child, ItemId request, string asks, ParentEscalation escalation, string[] details) =>
        string.Join(
            '\n',
            [
                $"Sub-agent \"{Title(child)}\" is waiting for you: {asks}",
                $"It is job {Job(child)}, from your delegate call {child.Item.Value}. Answer with the answer_child tool: allow it only if you would be allowed to do it yourself, deny it with an optional message saying what to do instead, or pass it to a person. Without your answer within {Seconds(escalation.Window)} seconds it goes to a person.",
                .. details.Where(detail => detail.Length > 0),
                $"answer_child {Example(child, request)}",
            ]);

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

    private static string Seconds(TimeSpan window) => window.TotalSeconds.ToString("0", CultureInfo.InvariantCulture);

    private static string Wants(ItemKind kind) => kind switch
    {
        ItemKind.Command => "run a command",
        ItemKind.FileEdit => "edit a file",
        ItemKind.Web => "reach the web",
        ItemKind.Mcp => "use a tool",
        _ => "do something",
    };
}
