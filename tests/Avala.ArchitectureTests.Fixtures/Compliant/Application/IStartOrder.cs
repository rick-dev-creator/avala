using Avala.Sdk;

namespace Avala.Fixtures.Compliant.Application;

public interface IStartOrder
{
    ValueTask<Result<OrderSummary, OrderFailure>> ExecuteAsync(string line, CancellationToken cancellationToken);
}
