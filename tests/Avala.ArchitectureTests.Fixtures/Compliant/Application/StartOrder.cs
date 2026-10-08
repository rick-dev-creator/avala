using Avala.Fixtures.Compliant.Domain;
using Avala.Sdk;

namespace Avala.Fixtures.Compliant.Application;

public sealed class StartOrder(IOrderRepository orders)
{
    public async ValueTask<Result<OrderSummary, OrderFailure>> ExecuteAsync(string line, CancellationToken cancellationToken)
    {
        if (!Order.Create(new OrderId(Guid.NewGuid())).TryGetValue(out var order, out _)
            || order.AddLine(line).IsFailure)
        {
            return OrderFailure.Rejected;
        }

        await orders.SaveAsync(order, cancellationToken);

        return new OrderSummary(order.Id.Value, order.Lines.Count);
    }
}
