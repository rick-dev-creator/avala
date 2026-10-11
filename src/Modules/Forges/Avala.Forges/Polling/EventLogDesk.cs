using Avala.Forges.Contracts;
using Avala.Sdk;
using Avala.Triggers.Contracts;

namespace Avala.Forges.Polling;

internal sealed class EventLogDesk(EventBook book, ForgeEventPoller poller) : IForgeEventLog
{
    public IReadOnlyList<ForgeEventWatch> Watches() => book.Watches;

    public IReadOnlyList<ForgeEventEntry> Events() => book.Entries;

    public async ValueTask<Result<ForgeEventWatch, ForgeError>> CheckNowAsync(TriggerId trigger, CancellationToken cancellationToken) =>
        await poller.CheckNowAsync(trigger, cancellationToken);
}
