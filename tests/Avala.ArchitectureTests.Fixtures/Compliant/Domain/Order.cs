using Avala.Sdk;
using Avala.Sdk.Domain;
using Stateless;

namespace Avala.Fixtures.Compliant.Domain;

public sealed class Order : IAggregateRoot<OrderId>
{
    private readonly List<string> lines = [];
    private readonly StateMachine<OrderState, OrderTrigger> machine;

    private Order(OrderId id)
    {
        Id = id;
        machine = new StateMachine<OrderState, OrderTrigger>(() => State, state => State = state);
        machine.Configure(OrderState.Draft).PermitIf(OrderTrigger.Place, OrderState.Placed, () => lines.Count > 0);
    }

    public OrderId Id { get; }

    public OrderState State { get; private set; }

    public IReadOnlyList<string> Lines => lines;

    public static Result<Order, OrderError> Create(OrderId id) => new Order(id);

    public Result<LineAdded, OrderError> AddLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return OrderError.EmptyLine;
        }

        lines.Add(line);

        return new LineAdded(Id, line);
    }

    public Result<OrderPlaced, OrderError> Place() =>
        machine.TryFire(OrderTrigger.Place, OrderError.InvalidTransition).Map(_ => new OrderPlaced(Id));
}
