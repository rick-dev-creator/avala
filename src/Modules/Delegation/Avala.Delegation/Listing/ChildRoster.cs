using Avala.Delegation.Contracts;
using Avala.Delegation.Escalating;
using Avala.Delegation.Policy;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Sdk;

namespace Avala.Delegation.Listing;

internal sealed class ChildRoster(IDelegations delegations, IJobCatalog catalog, IUsageSessions sessions)
{
    public async Task<IReadOnlyList<ListedSubagent>> OfAsync(JobId parent, Func<JobId, Asking> asking, CancellationToken cancellationToken)
    {
        var children = (await catalog.ChildrenAsync(parent, cancellationToken)).ToDictionary(child => child.Job);
        var seen = sessions.Sessions();

        return
        [
            .. delegations.OfParent(parent).SelectMany(record => record.Child.Match<ListedSubagent[]>(
                child => [Listed(record, child, children.TryGetValue(child, out var summary) ? summary : Option<JobSummary>.None, asking(child), seen)],
                () => [])),
        ];
    }

    private static ListedSubagent Listed(DelegationRecord record, JobId child, Option<JobSummary> summary, Asking asking, IReadOnlyList<UsageSession> seen)
    {
        var status = summary.Map(found => found.Status).Match(found => found, () => record.Report.Match(report => report.Status, () => JobStatus.Running));
        var hold = summary.Bind(found => found.Hold).Match(Option<HoldReason>.Some, () => record.Report.Bind(report => report.Hold));
        var state = Lifecycle.Of(status, hold, asking);

        return new ListedSubagent(ChildNotes.Title(record), SubagentKind.CrossHarness, state, Lifecycle.Status(state, asking, hold))
        {
            Job = child,
            Connection = record.Connection.Match(Option<Agents.Contracts.Connections.ConnectionName>.Some, () => summary.Bind(found => found.Connection)),
            Harness = seen.LastOrDefault(session => session.Job == Option<JobId>.Some(child)) is { } found ? found.Provider.Name : Option<string>.None,
            Role = record.Role,
        };
    }
}
