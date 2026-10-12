using Avala.Components.UI.Theme;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Threading;

namespace Avala.Components.UI;

public sealed class Stage : Panel
{
    public static readonly StyledProperty<object?> ContentProperty =
        AvaloniaProperty.Register<Stage, object?>(nameof(Content));

    public static readonly StyledProperty<bool> KeepsAliveProperty =
        AvaloniaProperty.Register<Stage, bool>(nameof(KeepsAlive));

    private readonly Dictionary<object, ContentPresenter> kept = new(ReferenceEqualityComparer.Instance);
    private ContentPresenter? shown;
    private bool posted;

    public object? Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    public bool KeepsAlive
    {
        get => GetValue(KeepsAliveProperty);
        set => SetValue(KeepsAliveProperty, value);
    }

    public object? Presented => shown?.Content;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ContentProperty && !posted)
        {
            posted = true;
            Dispatcher.UIThread.Post(Present, DispatcherPriority.Background);
        }
    }

    private void Present()
    {
        posted = false;
        var next = Content;

        if (ReferenceEquals(next, shown?.Content) && shown is not null)
        {
            return;
        }

        Retire(shown);
        shown = next is null ? null : Presenter(next);

        if (shown is not null)
        {
            shown.IsVisible = true;
            Motion.Reveal(shown);
        }
    }

    private ContentPresenter Presenter(object content)
    {
        if (KeepsAlive && kept.TryGetValue(content, out var known))
        {
            return known;
        }

        var presenter = new ContentPresenter { Content = content };
        Motion.Settle(presenter);
        Children.Add(presenter);

        if (KeepsAlive)
        {
            kept[content] = presenter;
        }

        return presenter;
    }

    private void Retire(ContentPresenter? presenter)
    {
        if (presenter is null)
        {
            return;
        }

        if (KeepsAlive && presenter.Content is { } content && kept.ContainsKey(content))
        {
            presenter.IsVisible = false;

            return;
        }

        Children.Remove(presenter);
    }
}
