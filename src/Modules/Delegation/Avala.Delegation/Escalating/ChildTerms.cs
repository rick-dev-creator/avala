using System.Collections.Immutable;
using Avala.Delegation.Contracts;
using Avala.Delegation.Records;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;

namespace Avala.Delegation.Escalating;

internal sealed class ChildTerms(DelegationBook book, IJobCatalog catalog) : IJobTerms
{
    private ImmutableDictionary<JobId, DelegationRecord> submitting = ImmutableDictionary<JobId, DelegationRecord>.Empty;

    public void Submitting(JobId parent, DelegationRecord planned) =>
        ImmutableInterlocked.AddOrUpdate(ref submitting, parent, planned, (_, _) => planned);

    public void Submitted(JobId parent) => ImmutableInterlocked.TryRemove(ref submitting, parent, out _);

    public async ValueTask<JobTerms> OfAsync(JobId job, CancellationToken cancellationToken)
    {
        await book.RestoreAsync(cancellationToken);

        return await book.OfChild(job).Match(
            known => Task.FromResult(Of(known)),
            async () => (await catalog.HistoryAsync(job, cancellationToken))
                .Bind(history => history.Summary.Parent)
                .Bind(parent => Volatile.Read(ref submitting).TryGetValue(parent, out var planned) ? planned : Option<DelegationRecord>.None)
                .Match(Of, () => JobTerms.Full));
    }

    private static JobTerms Of(DelegationRecord record) =>
        new(record.ReadOnly, record.Escalation.IsSome ? record.Parent : Option<JobId>.None);
}
