using Avala.Sdk;

namespace Avala.Observability.Tracking;

internal sealed class UsageRestore(UsageBook book, IUsageStore store) : IStartupTask
{
    public async Task RunAsync(CancellationToken cancellationToken) => book.Restore(await store.EarlierRunsAsync(cancellationToken));
}
