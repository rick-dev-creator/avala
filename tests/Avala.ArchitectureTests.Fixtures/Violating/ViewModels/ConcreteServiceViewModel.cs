using Avala.Fixtures.Violating.Application;

namespace Avala.Fixtures.Violating.ViewModels;

public sealed class ConcreteServiceViewModel(LedgerService service)
{
    public string Title => service.Name;
}
