using Avalonia.Controls;
using Avalonia.Input;

namespace Avala.Host;

internal sealed partial class ShellView : Window
{
    public ShellView() => InitializeComponent();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.F11 || (e.Key == Key.F && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Meta)))
        {
            WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
            e.Handled = true;

            return;
        }

        base.OnKeyDown(e);
    }
}
