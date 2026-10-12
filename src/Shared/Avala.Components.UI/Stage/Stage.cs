using Avala.Components.UI.Theme;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Threading;

namespace Avala.Components.UI;

public sealed class Stage : Panel
{
    public static readonly StyledProperty<object> ContentProperty =
        AvaloniaProperty.Register<Stage, object>(nameof(Content));

    public static readonly StyledProperty<int> KeepsProperty =
        AvaloniaProperty.Register<Stage, int>(nameof(Keeps));

    private readonly List<ContentPresenter> kept = [];
    private ContentPresenter? shown;
    private ContentPresenter? arriving;
    private ContentPresenter? leaving;
    private CancellationTokenSource? fading;
    private bool posted;

    public object Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    public int Keeps
    {
        get => GetValue(KeepsProperty);
        set => SetValue(KeepsProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ContentProperty && !posted)
        {
            posted = true;
            Dispatcher.UIThread.Post(Present, DispatcherPriority.Background);
        }
        else if (change.Property == Motion.IsReducedProperty && Motion.GetIsReduced(this))
        {
            Finish();
        }
    }

    private void Present()
    {
        posted = false;
        var next = Content;

        if (ReferenceEquals(next, arriving?.Content) && arriving is not null)
        {
            return;
        }

        Finish();
        Abandon();

        if (ReferenceEquals(next, shown?.Content) && shown is not null)
        {
            return;
        }

        if (next is null)
        {
            var gone = shown;
            shown = null;
            Retire(gone);

            return;
        }

        var presenter = Presenter(next);
        arriving = presenter;
        presenter.Opacity = 0;
        presenter.IsHitTestVisible = false;
        presenter.IsVisible = true;
        presenter.LayoutUpdated += OnArrivingLaidOut;
    }

    private void OnArrivingLaidOut(object? sender, EventArgs e)
    {
        if (sender is ContentPresenter presenter)
        {
            presenter.LayoutUpdated -= OnArrivingLaidOut;
            Dispatcher.UIThread.Post(() => Ready(presenter), DispatcherPriority.Background);
        }
    }

    private void Ready(ContentPresenter presenter)
    {
        if (!ReferenceEquals(presenter, arriving))
        {
            return;
        }

        if (!Motion.GetIsReduced(this) && TopLevel.GetTopLevel(this) is { } top)
        {
            top.RequestAnimationFrame(_ => Begin(presenter, fades: true));
        }
        else
        {
            Begin(presenter, fades: false);
        }
    }

    private void Begin(ContentPresenter presenter, bool fades)
    {
        if (!ReferenceEquals(presenter, arriving))
        {
            return;
        }

        arriving = null;
        var previous = shown;
        shown = presenter;
        presenter.IsHitTestVisible = true;
        presenter.Opacity = 1;

        if (previous is not null)
        {
            previous.IsHitTestVisible = false;
            previous.Opacity = 0;
        }

        if (fades && !Motion.GetIsReduced(this))
        {
            _ = CrossFadeAsync(presenter, previous);
        }
        else
        {
            Retire(previous);
        }

        Trim();
    }

    private async Task CrossFadeAsync(ContentPresenter incoming, ContentPresenter? outgoing)
    {
        using var cancellation = new CancellationTokenSource();
        fading = cancellation;
        leaving = outgoing;
        var entering = MotionCatalog.StageIn.Animation.RunAsync(incoming, cancellation.Token);
        var exiting = outgoing is null ? Task.CompletedTask : MotionCatalog.StageOut.Animation.RunAsync(outgoing, cancellation.Token);
        await Task.WhenAll(entering, exiting);

        if (ReferenceEquals(fading, cancellation))
        {
            fading = null;
            leaving = null;
            Retire(outgoing);
        }
    }

    private void Finish()
    {
        if (fading is { } running)
        {
            fading = null;
            running.Cancel();
            var gone = leaving;
            leaving = null;
            Retire(gone);
        }
    }

    private void Abandon()
    {
        if (arriving is { } abandoned)
        {
            arriving = null;
            abandoned.LayoutUpdated -= OnArrivingLaidOut;
            Retire(abandoned);
        }
    }

    private ContentPresenter Presenter(object content)
    {
        if (kept.Find(presenter => ReferenceEquals(presenter.Content, content)) is { } known)
        {
            kept.Remove(known);
            kept.Add(known);

            return known;
        }

        var created = new ContentPresenter { Content = content, RenderTransform = MotionCatalog.Transforms() };
        Motion.Settle(created);
        Children.Add(created);

        if (Keeps > 0)
        {
            kept.Add(created);
        }

        return created;
    }

    private void Retire(ContentPresenter? presenter)
    {
        if (presenter is null || ReferenceEquals(presenter, shown) || ReferenceEquals(presenter, arriving))
        {
            return;
        }

        if (kept.Contains(presenter))
        {
            presenter.IsVisible = false;

            return;
        }

        Children.Remove(presenter);
    }

    private void Trim()
    {
        while (kept.Count > Keeps && kept.Find(presenter => !presenter.IsVisible) is { } oldest)
        {
            kept.Remove(oldest);
            Children.Remove(oldest);
        }
    }
}
