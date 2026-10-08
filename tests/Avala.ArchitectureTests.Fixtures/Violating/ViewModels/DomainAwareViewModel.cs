using Avala.Fixtures.Violating.Domain;

namespace Avala.Fixtures.Violating.ViewModels;

public sealed class DomainAwareViewModel(LedgerId ledger)
{
    public LedgerId Ledger { get; } = ledger;
}
