using Avala.Fixtures.Compliant.Application;
using Avala.Fixtures.Compliant.Domain;

namespace Avala.Fixtures.Compliant.Infrastructure;

public sealed class SqliteOrderRepository : IOrderRepository
{
    private readonly OrdersDbContext context = new();

    public async ValueTask SaveAsync(Order order, CancellationToken cancellationToken) =>
        await Task.Run(
            async () =>
            {
                await context.Orders.AddAsync(order, cancellationToken);
                await context.SaveChangesAsync(cancellationToken);
            },
            cancellationToken);
}
