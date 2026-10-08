using Avala.Fixtures.Violating.Infrastructure;

namespace Avala.Fixtures.Violating.Application;

public sealed class InfrastructureAwareService(LedgerStore store)
{
    public LedgerStore Store { get; } = store;
}
