using System.Collections.Immutable;
using Avala.Fixtures.Compliant.Application;
using Avala.Fixtures.Compliant.Domain;

namespace Avala.Fixtures.Compliant.Infrastructure;

public sealed class InMemoryOrderRepository : IOrderRepository
{
    private ImmutableList<Order> saved = [];

    public IReadOnlyList<Order> Saved => Volatile.Read(ref saved);

    public ValueTask SaveAsync(Order order, CancellationToken cancellationToken)
    {
        ImmutableInterlocked.Update(ref saved, orders => orders.Add(order));

        return ValueTask.CompletedTask;
    }
}
