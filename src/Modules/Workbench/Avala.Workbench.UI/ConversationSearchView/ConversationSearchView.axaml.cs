using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Avala.Workbench.UI;

internal sealed partial class ConversationSearchView : UserControl
{
    public ConversationSearchView()
    {
        InitializeComponent();
        Bar.PropertyChanged += OnBarChanged;
    }

    private void OnBarChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty && Bar.IsVisible)
        {
            Dispatcher.UIThread.Post(() =>
            {
                Query.Focus();
                Query.SelectAll();
            });
        }
    }
}
