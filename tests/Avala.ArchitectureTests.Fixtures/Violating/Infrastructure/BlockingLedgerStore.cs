namespace Avala.Fixtures.Violating.Infrastructure;

public sealed class BlockingLedgerStore
{
    private readonly LedgerDbContext context = new();

    public Task<int> SaveAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);
}
