using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Avala.Fixtures.Compliant.Views;

public sealed partial class OrderView : UserControl
{
    public OrderView() => InitializeComponent();

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        Opacity = 1;
    }
}
