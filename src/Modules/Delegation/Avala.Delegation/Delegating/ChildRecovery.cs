using Avala.Delegation.Records;
using Avala.Jobs.Contracts;

namespace Avala.Delegation.Delegating;

internal sealed class ChildRecovery(DelegationBook book) : IChildRetries
{
    public async ValueTask<bool> RetriesAsync(JobId child, CancellationToken cancellationToken)
    {
        await book.RestoreAsync(cancellationToken);

        return book.OfChild(child).Match(record => record.Report.IsNone, () => false);
    }
}
