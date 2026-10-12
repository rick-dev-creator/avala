using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Avala.Workbench.UI;

internal sealed partial class OnboardingView : UserControl
{
    public OnboardingView() => InitializeComponent();

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        FocusContinue();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsVisibleProperty)
        {
            FocusContinue();
        }
    }

    private void FocusContinue()
    {
        if (IsVisible)
        {
            Dispatcher.UIThread.Post(() => Continue.Focus(), DispatcherPriority.Input);
        }
    }
}
