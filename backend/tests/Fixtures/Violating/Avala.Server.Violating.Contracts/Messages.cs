using Ardalis.Result;
using Mediator;

namespace Avala.Server.Violating.Contracts;

internal sealed record PlaceOrder(string Code) : IRequest<Result>;

internal sealed record CountOrders : IQuery<int>;

internal sealed record ArchiveOrder(Guid Id) : ICommand<Result<ArchivedOrder>>;

internal sealed record ArchivedOrder(Guid Id);

internal sealed record ShipOrder(Guid Id) : ICommand<Result>;

internal sealed class ShipOrderHandler : ICommandHandler<ShipOrder, Result>
{
    public ValueTask<Result> Handle(ShipOrder command, CancellationToken cancellationToken) => ValueTask.FromResult(Result.Success());
}
