using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Avala.Workbench.UI;

internal sealed partial class ReviewView : UserControl
{
    public ReviewView()
    {
        InitializeComponent();
        WriteFeedback.IsCheckedChanged += (_, _) => FocusWhen(WriteFeedback.IsChecked == true, Feedback);
        DiscardConfirmation.PropertyChanged += (_, change) =>
        {
            if (change.Property == IsVisibleProperty)
            {
                FocusWhen(true, DiscardConfirmation.IsVisible ? CancelDiscard : RequestDiscard);
            }
        };
        Stamp.PropertyChanged += (_, change) =>
        {
            if (change.Property == IsVisibleProperty)
            {
                FocusWhen(Stamp.IsVisible, BackToConversations);
            }
        };
    }

    private static void FocusWhen(bool shown, Control target)
    {
        if (shown)
        {
            Dispatcher.UIThread.Post(() => target.Focus());
        }
    }
}
