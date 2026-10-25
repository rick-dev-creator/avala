using Ardalis.Result;
using Avala.Server.Violating.Contracts;
using Avala.Server.Violating.Domain;
using Avala.Server.Violating.Infrastructure;
using Mediator;

namespace Avala.Server.Violating.Application;

internal interface IOrderRepository
{
    Order Find(Guid id);
}

internal sealed class OrderReminder(OrdersDbContext database)
{
    public int Pending => database.Orders.Count();
}

internal sealed class PlaceOrderHandler : IRequestHandler<PlaceOrder, Result>
{
    public ValueTask<Result> Handle(PlaceOrder request, CancellationToken cancellationToken) => ValueTask.FromResult(Result.Success());
}
