using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Avala.Workbench.UI;

internal sealed partial class ConversationView : UserControl
{
    private const double LiveEdge = 24;

    public ConversationView()
    {
        InitializeComponent();
        Scroller.ScrollChanged += OnScrollChanged;
        JumpToLatest.Click += OnJumpToLatest;
    }

    public bool FollowsLiveEdge { get; private set; } = true;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Follow();
    }

    private void OnJumpToLatest(object? sender, RoutedEventArgs e) => Follow();

    private void Follow()
    {
        FollowsLiveEdge = true;
        NewBelow.IsVisible = false;
        Jump.IsVisible = false;
        Scroller.ScrollToEnd();
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentDelta.Y != 0 || e.ViewportDelta.Y != 0)
        {
            if (FollowsLiveEdge)
            {
                Scroller.ScrollToEnd();
            }
            else if (e.ExtentDelta.Y > 0)
            {
                NewBelow.IsVisible = true;
            }

            return;
        }

        FollowsLiveEdge = Scroller.Offset.Y >= Scroller.Extent.Height - Scroller.Viewport.Height - LiveEdge;
        Jump.IsVisible = !FollowsLiveEdge;
        NewBelow.IsVisible = NewBelow.IsVisible && !FollowsLiveEdge;
    }
}
