namespace Avala.Fixtures.Compliant.Contracts;

public interface IOrders
{
    ValueTask<int> CountAsync(CancellationToken cancellationToken);
}
