using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Delegation.Contracts;
using Avala.Delegation.Policy;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;

namespace Avala.Delegation.Delegating;

internal sealed record Caller(SessionId Session, Option<JobId> Job, Option<string> Worktree, int Pending);

internal sealed record Plan(JobSummary Parent, Option<ConnectionName> Connection, Autonomy Autonomy);

internal sealed record Decision(string Instruction, int Depth, Result<Plan, DelegationError> Outcome);

internal sealed class DelegationPolicy(IDelegationRules rules, IJobCatalog catalog, IPermissionAudit audit, ConnectionGauge gauge)
{
    public async Task<Decision> DecideAsync(Caller caller, string input, CancellationToken cancellationToken)
    {
        if (!DelegationInput.Parse(input).TryGetValue(out var asked, out var malformed))
        {
            return new Decision(string.Empty, 0, malformed);
        }

        var parent = await caller.Job.Match(
            job => catalog.HistoryAsync(job, cancellationToken).AsTask(),
            () => Task.FromResult(Option<JobHistory>.None));

        if (!parent.ToResult(DelegationError.NoJob).TryGetValue(out var history, out _)
            || !caller.Worktree.ToResult(DelegationError.NoJob).TryGetValue(out var worktree, out _))
        {
            return new Decision(asked.Instruction, 0, DelegationError.NoJob);
        }

        var depth = await DepthOfAsync(history.Summary, cancellationToken) + 1;

        if (!(await rules.OfWorktreeAsync(worktree, cancellationToken))
                .Bind(declared => declared.ToResult(DelegationError.NotDeclared))
                .TryGetValue(out var delegation, out var undeclared))
        {
            return new Decision(asked.Instruction, depth, undeclared);
        }

        var granted = audit.AutonomyOf(caller.Session).Match(applied => applied.Effective, () => Autonomy.Supervised);
        var refused = delegation.Refuses(depth, caller.Pending, asked.Autonomy, granted);

        if (refused.IsSome)
        {
            return new Decision(asked.Instruction, depth, refused.Match(error => error, () => default));
        }

        var earlier = (await catalog.ChildrenAsync(history.Summary.Job, cancellationToken)).Count;
        var connection = delegation.Route(earlier, gauge.Used).Match(Option<ConnectionName>.Some, () => history.Summary.Connection);

        return new Decision(asked.Instruction, depth, new Plan(history.Summary, connection, asked.Autonomy.Match(stricter => stricter, () => granted)));
    }

    private async Task<int> DepthOfAsync(JobSummary job, CancellationToken cancellationToken)
    {
        var depth = 0;
        var above = job.Parent;

        while (above.IsSome)
        {
            var history = await above.Match(
                parent => catalog.HistoryAsync(parent, cancellationToken).AsTask(),
                () => Task.FromResult(Option<JobHistory>.None));
            depth++;
            above = history.Bind(found => found.Summary.Parent);
        }

        return depth;
    }
}
