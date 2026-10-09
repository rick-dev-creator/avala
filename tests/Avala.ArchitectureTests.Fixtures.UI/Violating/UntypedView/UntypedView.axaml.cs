using Avala.Fixtures.Violating.Application;
using Avalonia.Controls;

namespace Avala.Fixtures.Violating.Views;

public sealed class UntypedView : UserControl
{
    private readonly LedgerService ledger;

    public UntypedView(LedgerService ledger) => this.ledger = ledger;

    public void OnSaveClicked(object sender, object args) => Tag = ledger.Name;
}
