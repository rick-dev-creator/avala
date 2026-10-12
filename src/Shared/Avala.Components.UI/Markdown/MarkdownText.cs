using System.Windows.Input;
using Avala.Components.UI.Copying;
using Avala.Components.UI.Theme;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Markdig;
using MarkView.Avalonia;
using MarkView.Avalonia.Rendering;

namespace Avala.Components.UI.Markdown;

public sealed class MarkdownText : StackPanel
{
    public static readonly StyledProperty<string> MarkdownProperty =
        AvaloniaProperty.Register<MarkdownText, string>(nameof(Markdown), string.Empty);

    public static readonly StyledProperty<bool> IsLiveProperty =
        AvaloniaProperty.Register<MarkdownText, bool>(nameof(IsLive));

    public static readonly StyledProperty<ICommand> LinkCommandProperty =
        AvaloniaProperty.Register<MarkdownText, ICommand>(nameof(LinkCommand), OpensNothing.Instance);

    private readonly MarkdownViewer settled;
    private readonly MarkdownViewer tail;
    private readonly Border caret;
    private bool styled;

    public MarkdownText()
        : this(OfflineMarkdown.WithoutImages)
    {
    }

    public MarkdownText(MarkdownPipeline pipeline)
    {
        settled = Viewer("Settled", pipeline);
        tail = Viewer("Tail", pipeline);
        tail.IsVisible = false;
        Children.Add(settled);
        Children.Add(tail);
        caret = new Border { Name = "Caret", Width = 2, Height = 16, HorizontalAlignment = HorizontalAlignment.Left, IsVisible = false };
        Motion.SetLoop(caret, MotionLoop.Caret);
        caret.Bind(Border.BackgroundProperty, caret.GetResourceObservable("AccentBrush"));
        Children.Add(caret);
        AddHandler(MarkdownViewer.LinkClickedEvent, OnLinkClicked);
        AddHandler(PointerReleasedEvent, (_, _) => Show(Markdown, IsLive), RoutingStrategies.Bubble, handledEventsToo: true);
        ContextFlyout = SelectionMenu.For(() => _ = CopyAsync(), SelectAll, () => SelectedText.Length > 0);
    }

    public string SelectedText => string.Concat(settled.GetSelectedText(), tail.IsVisible ? tail.GetSelectedText() : string.Empty);

    public void SelectAll()
    {
        settled.SelectAll();

        if (tail.IsVisible)
        {
            tail.SelectAll();
        }
    }

    public async Task CopyAsync()
    {
        if (SelectedText is { Length: > 0 } selected && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(selected);
        }
    }

    public string Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    public bool IsLive
    {
        get => GetValue(IsLiveProperty);
        set => SetValue(IsLiveProperty, value);
    }

    public ICommand LinkCommand
    {
        get => GetValue(LinkCommandProperty);
        set => SetValue(LinkCommandProperty, value);
    }

    protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);

        if (!styled && !MarkdownStyles.GetAreShared(this))
        {
            styled = true;
            Styles.Add(MarkdownStyles.Include());
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == MarkdownProperty || change.Property == IsLiveProperty)
        {
            Show(Markdown, IsLive);
        }
    }

    private static MarkdownViewer Viewer(string name, MarkdownPipeline pipeline)
    {
        var viewer = new MarkdownViewer { Name = name, Pipeline = pipeline };
        viewer.Extensions.Add(CopyableCode.Instance);

        return viewer;
    }

    private void Show(string text, bool live)
    {
        var cut = Cut(text, live);
        var done = text[..cut];
        var growing = text[cut..];

        if (settled.Markdown != done)
        {
            settled.Markdown = done;
        }

        tail.Markdown = growing;
        tail.IsVisible = growing.Length > 0;
        caret.IsVisible = live;
    }

    private int Cut(string text, bool live)
    {
        var kept = settled.Markdown ?? string.Empty;
        var holding = kept.Length > 0
            && settled.GetSelectedText().Length > 0
            && text.StartsWith(kept, StringComparison.Ordinal);

        return holding ? kept.Length : live ? MarkdownBlocks.Settled(text) : text.Length;
    }

    private void OnLinkClicked(object? sender, LinkClickedEventArgs link)
    {
        link.Handled = true;

        if (LinkCommand.CanExecute(link.Url))
        {
            LinkCommand.Execute(link.Url);
        }
    }
}
