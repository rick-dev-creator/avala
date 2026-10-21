using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Delegation.Contracts;
using Avala.Delegation.Records;
using Avala.Delegation.Reporting;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Delegation.Escalating;

internal sealed class ChildStops(ChildReporter reporter, DelegationJournal journal, IPermissionAudit audit) : IHandle<AgentActivity>
{
    public async ValueTask HandleAsync(AgentActivity integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.Event is not ToolCalled { Tool: StopChildTool.Name } called)
        {
            return;
        }

        var call = new CallRef(called.Session, called.Item);
        var caller = audit.AutonomyOf(called.Session).Map(applied => applied.Job);
        var child = ChildInput.Parse(called.Input).Bind(journal.OfChild).Bind(record => caller.IsSome && record.Parent == caller ? record : Option<DelegationRecord>.None);

        await child.Match(
            record =>
            {
                reporter.Stop(record, call, cancellationToken);

                return Task.CompletedTask;
            },
            () => journal.ReturnAsync(call, ChildInput.NotYourChild(called.Item), cancellationToken));
    }
}
