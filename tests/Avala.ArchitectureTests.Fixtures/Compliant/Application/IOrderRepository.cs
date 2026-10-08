using Avala.Fixtures.Compliant.Domain;

namespace Avala.Fixtures.Compliant.Application;

public interface IOrderRepository
{
    ValueTask SaveAsync(Order order, CancellationToken cancellationToken);
}
