using System.Text.Json;
using System.Text.Json.Nodes;
using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Delegation.Contracts;
using Avala.Delegation.Records;
using Avala.Delegation.Reporting;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Delegation.Escalating;

internal sealed class ChildWaits(ChildReporter reporter, DelegationJournal journal, IPermissionAudit audit, IParentAnswers answers) : IHandle<AgentActivity>
{
    public async ValueTask HandleAsync(AgentActivity integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.Event is not ToolCalled { Tool: WaitChildTool.Name } called)
        {
            return;
        }

        var call = new CallRef(called.Session, called.Item);
        var caller = audit.AutonomyOf(called.Session).Map(applied => applied.Job);
        var child = ChildIn(called.Input).Bind(journal.OfChild).Bind(record => caller.IsSome && record.Parent == caller ? record : Option<DelegationRecord>.None);

        await child.Match(
            record =>
            {
                reporter.Wait(record, call, Queued(record), cancellationToken);

                return Task.CompletedTask;
            },
            () => journal.ReturnAsync(call, Refused(called.Item), cancellationToken));
    }

    private Option<ToolResult> Queued(DelegationRecord record) =>
        record.Child.Bind(journal.Calls.TakeQueued).Bind(asking => answers.Waits(asking.Session, asking.Request) ? asking.Result : Option<ToolResult>.None);

    private static Option<JobId> ChildIn(string input)
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

    private static ToolResult Refused(ItemId item) =>
        new(item, new JsonObject { ["refused"] = "notYourChild", ["reason"] = "the input needs child, the job of a sub-agent you delegated to, as the early result named it." }.ToJsonString()) { IsError = true };
}
