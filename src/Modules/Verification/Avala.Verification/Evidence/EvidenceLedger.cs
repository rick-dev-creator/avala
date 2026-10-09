using Avala.Sdk.Events;
using Avala.Verification.Contracts;

namespace Avala.Verification.Evidence;

internal sealed class EvidenceLedger(EvidenceBook book, IEventBus bus)
{
    public async Task RecordAsync(VerificationReport report, CancellationToken cancellationToken)
    {
        book.Keep(report);
        await bus.PublishAsync(new AttemptVerified(report), cancellationToken);
    }
}
