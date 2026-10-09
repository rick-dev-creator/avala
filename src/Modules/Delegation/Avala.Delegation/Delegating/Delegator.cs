using Avala.Agents.Contracts.Events;
using Avala.Delegation.Contracts;
using Avala.Delegation.Records;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Delegation.Delegating;

internal sealed record Delegated(JobId Child, DelegationRecord Record);

internal sealed class Delegator(DelegationPolicy policy, IJobs jobs, DelegationJournal journal)
{
    public async Task<Option<Delegated>> DelegateAsync(ToolCalled called, Caller caller, CancellationToken cancellationToken)
    {
        var decision = await policy.DecideAsync(caller, called.Input, cancellationToken);
        var record = new DelegationRecord(called.Session, called.Item, decision.Instruction, journal.Now) { Parent = caller.Job, Depth = decision.Depth };

        if (!decision.Outcome.TryGetValue(out var plan, out var refusal))
        {
            await journal.RefusedAsync(record with { Refusal = refusal }, refusal, cancellationToken);

            return Option<Delegated>.None;
        }

        var planned = record with { Connection = plan.Connection, Autonomy = plan.Autonomy };
        var request = new JobRequest(plan.Parent.Repository, decision.Instruction)
        {
            Parent = plan.Parent.Job,
            Connection = plan.Connection,
            Autonomy = plan.Autonomy,
        };

        if (!(await jobs.SubmitAsync(request, cancellationToken)).TryGetValue(out var child, out var rejection))
        {
            await journal.RefusedAsync(planned with { Refusal = DelegationError.NotSubmitted, Rejection = rejection }, DelegationError.NotSubmitted, cancellationToken);

            return Option<Delegated>.None;
        }

        var delegated = planned with { Child = child };
        await journal.DelegatedAsync(delegated, cancellationToken);

        return new Delegated(child, delegated);
    }
}
