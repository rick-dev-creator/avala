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

    public static readonly StyledProperty<ICommand> CodeCommandProperty =
        AvaloniaProperty.Register<MarkdownText, ICommand>(nameof(CodeCommand), OpensNothing.Instance);

    private const double BlockSpacing = 8;

    private readonly MarkdownPipeline pipeline;
    private readonly MarkdownViewer settled;
    private readonly StackPanel streaming;
    private readonly MarkdownViewer tail;
    private readonly Border caret;
    private string kept = string.Empty;
    private bool styled;

    public MarkdownText()
        : this(OfflineMarkdown.WithoutImages)
    {
    }

    public MarkdownText(MarkdownPipeline pipeline)
    {
        this.pipeline = pipeline;
        settled = Viewer("Settled");
        tail = Viewer("Tail");
        tail.IsVisible = false;
        streaming = new StackPanel { Name = "Streaming", Spacing = BlockSpacing, IsVisible = false };
        streaming.Children.Add(tail);
        Children.Add(settled);
        Children.Add(streaming);
        caret = new Border { Name = "Caret", Width = 2, Height = 16, HorizontalAlignment = HorizontalAlignment.Left, IsVisible = false };
        Motion.SetLoop(caret, MotionLoop.Caret);
        caret.Bind(Border.BackgroundProperty, caret.GetResourceObservable("AccentBrush"));
        Children.Add(caret);
        AddHandler(MarkdownViewer.LinkClickedEvent, OnLinkClicked);
        AddHandler(PointerReleasedEvent, (_, _) => Show(Markdown, IsLive), RoutingStrategies.Bubble, handledEventsToo: true);
        ContextFlyout = SelectionMenu.For(() => _ = CopyAsync(), SelectAll, () => SelectedText.Length > 0);
    }

    public string SelectedText =>
        string.Join("\n", Shown.Select(viewer => viewer.GetSelectedText()).Where(selected => selected.Length > 0));

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

    public ICommand CodeCommand
    {
        get => GetValue(CodeCommandProperty);
        set => SetValue(CodeCommandProperty, value);
    }

    private IEnumerable<MarkdownViewer> Shown =>
        settled.IsVisible ? [settled] : streaming.Children.OfType<MarkdownViewer>().Where(viewer => viewer.IsVisible);

    private bool Selecting =>
        streaming.Children.OfType<MarkdownViewer>().Any(viewer => viewer.GetSelectedText().Length > 0);

    public void SelectAll()
    {
        foreach (var viewer in Shown)
        {
            viewer.SelectAll();
        }
    }

    public async Task CopyAsync()
    {
        if (SelectedText is { Length: > 0 } selected && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(selected);
        }
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

    private MarkdownViewer Viewer(string name)
    {
        var viewer = new MarkdownViewer { Name = name, Pipeline = pipeline };
        viewer.Extensions.Add(CopyableCode.Instance);

        return viewer;
    }

    private void Show(string text, bool live)
    {
        if (!text.StartsWith(kept, StringComparison.Ordinal))
        {
            Forget();
        }

        if (live || (kept.Length > 0 && Selecting))
        {
            Stream(text, live ? MarkdownBlocks.Settled(text) : text.Length);
        }
        else
        {
            Settle(text);
        }

        caret.IsVisible = live;
    }

    private void Stream(string text, int cut)
    {
        if (cut > kept.Length)
        {
            var block = Viewer("Block");
            block.Markdown = text[kept.Length..cut];
            streaming.Children.Insert(streaming.Children.Count - 1, block);
            kept = text[..cut];
        }

        var growing = text[kept.Length..];
        tail.Markdown = growing;
        tail.IsVisible = growing.Length > 0;
        streaming.IsVisible = true;
        settled.IsVisible = false;
    }

    private void Settle(string text)
    {
        Forget();

        if (settled.Markdown != text)
        {
            settled.Markdown = text;
        }

        settled.IsVisible = true;
        streaming.IsVisible = false;
    }

    private void Forget()
    {
        if (streaming.Children.Count > 1)
        {
            streaming.Children.RemoveRange(0, streaming.Children.Count - 1);
        }

        kept = string.Empty;
        tail.Markdown = string.Empty;
        tail.IsVisible = false;
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
