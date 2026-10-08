using Avala.Fixtures.Violating.Infrastructure;

namespace Avala.Fixtures.Violating.ViewModels;

public sealed class InfrastructureAwareViewModel(LedgerStore store)
{
    public LedgerStore Store { get; } = store;
}
