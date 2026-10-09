using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Avala.Workbench.UI;

internal sealed partial class DecisionsView : UserControl
{
    public DecisionsView()
    {
        InitializeComponent();
        Items.SelectionChanged += (_, _) => FollowSelection();
        NoteBox.PropertyChanged += (_, change) =>
        {
            if (change.Property == IsVisibleProperty)
            {
                Dispatcher.UIThread.Post(() => (NoteBox.IsVisible ? DecisionNote : Selection()).Focus());
            }
        };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Dispatcher.UIThread.Post(() => Selection().Focus());
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsVisibleProperty && IsVisible)
        {
            Dispatcher.UIThread.Post(() => Selection().Focus());
        }
    }

    private void FollowSelection()
    {
        if (IsEffectivelyVisible && !DecisionNote.IsFocused && OwnsFocus())
        {
            Dispatcher.UIThread.Post(() => Selection().Focus());
        }
    }

    private bool OwnsFocus() =>
        TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not Visual focused
        || focused == this
        || this.IsVisualAncestorOf(focused);

    private Control Selection() =>
        Items.SelectedIndex >= 0 && Items.ContainerFromIndex(Items.SelectedIndex) is { } selected
            ? selected
            : this.GetVisualDescendants().OfType<Control>().FirstOrDefault(control => control.Focusable) ?? Items;
}
