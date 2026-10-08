using Avala.Fixtures.Compliant.Application;
using Avala.Fixtures.Compliant.Domain;

namespace Avala.Fixtures.Compliant.Infrastructure;

public sealed class InMemoryOrderRepository : IOrderRepository
{
    private readonly List<Order> saved = [];

    public ValueTask SaveAsync(Order order, CancellationToken cancellationToken)
    {
        saved.Add(order);

        return ValueTask.CompletedTask;
    }
}
