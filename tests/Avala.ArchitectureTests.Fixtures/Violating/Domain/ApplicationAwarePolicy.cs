using Avala.Fixtures.Violating.Application;

namespace Avala.Fixtures.Violating.Domain;

public sealed class ApplicationAwarePolicy(LedgerService service)
{
    public LedgerService Service { get; } = service;
}
