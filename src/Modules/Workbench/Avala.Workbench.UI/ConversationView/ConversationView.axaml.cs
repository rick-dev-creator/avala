using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Avala.Workbench.UI;

internal sealed partial class ConversationView : UserControl
{
    private const double LiveEdge = 24;

    private const double End = 1;

    public ConversationView()
    {
        InitializeComponent();
        Scroller.ScrollChanged += OnScrollChanged;
        Scroller.AddHandler(RequestBringIntoViewEvent, OnBringIntoView, RoutingStrategies.Bubble, handledEventsToo: true);
        JumpToLatest.Click += OnJumpToLatest;
        Entries.ItemsView.CollectionChanged += OnEntriesChanged;
    }

    public bool FollowsLiveEdge { get; private set; } = true;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Follow();
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        NewBelow.IsVisible = NewBelow.IsVisible || (!FollowsLiveEdge && e.Action == NotifyCollectionChangedAction.Add);

    private void OnJumpToLatest(object? sender, RoutedEventArgs e) => Follow();

    private void OnBringIntoView(object? sender, RequestBringIntoViewEventArgs e)
    {
        if (ReferenceEquals(e.TargetObject, Entries.ItemsPanelRoot) || !FollowsLiveEdge)
        {
            return;
        }

        FollowsLiveEdge = false;
        Dispatcher.UIThread.Post(Settle, DispatcherPriority.Background);
    }

    private void Settle()
    {
        FollowsLiveEdge = Scroller.Offset.Y >= Scroller.Extent.Height - Scroller.Viewport.Height - End;
        Jump.IsVisible = !FollowsLiveEdge;
    }

    private bool AtLiveEdge => Scroller.Offset.Y >= Scroller.Extent.Height - Scroller.Viewport.Height - LiveEdge;

    private void Follow()
    {
        FollowsLiveEdge = true;
        NewBelow.IsVisible = false;
        Jump.IsVisible = false;
        Scroller.ScrollToEnd();
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        var atLiveEdge = AtLiveEdge;

        if (e.OffsetDelta.Y < 0 && !atLiveEdge)
        {
            FollowsLiveEdge = false;
            Jump.IsVisible = true;

            return;
        }

        if (e.ExtentDelta.Y != 0 || e.ViewportDelta.Y != 0)
        {
            if (FollowsLiveEdge)
            {
                Scroller.ScrollToEnd();
            }

            return;
        }

        FollowsLiveEdge = atLiveEdge;
        Jump.IsVisible = !FollowsLiveEdge;
        NewBelow.IsVisible = NewBelow.IsVisible && !FollowsLiveEdge;
    }
}
