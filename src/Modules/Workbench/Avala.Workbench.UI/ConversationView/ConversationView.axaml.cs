using Avalonia.Controls;

namespace Avala.Workbench.UI;

internal sealed partial class ConversationView : UserControl
{
    private const double LiveEdge = 24;

    public ConversationView()
    {
        InitializeComponent();
        Scroller.ScrollChanged += OnScrollChanged;
    }

    public bool FollowsLiveEdge { get; private set; } = true;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        FollowsLiveEdge = true;
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

            return;
        }

        FollowsLiveEdge = Scroller.Offset.Y >= Scroller.Extent.Height - Scroller.Viewport.Height - LiveEdge;
    }
}
