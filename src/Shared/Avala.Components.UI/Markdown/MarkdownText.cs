using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Styling;
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

    private static readonly Uri Base = new("avares://Avala.Components.UI/");

    private readonly MarkdownViewer settled;
    private readonly MarkdownViewer tail;
    private readonly Border caret;

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
        caret = new Border { Name = "Caret", Width = 2, Height = 16, HorizontalAlignment = HorizontalAlignment.Left, IsVisible = false, Classes = { "caret" } };
        caret.Bind(Border.BackgroundProperty, caret.GetResourceObservable("AccentBrush"));
        Children.Add(caret);
        Styles.Add(new StyleInclude(Base) { Source = new Uri("Markdown/MarkdownStyles.axaml", UriKind.Relative) });
        AddHandler(MarkdownViewer.LinkClickedEvent, OnLinkClicked);
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
        var cut = live ? MarkdownBlocks.Settled(text) : text.Length;
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

    private void OnLinkClicked(object? sender, LinkClickedEventArgs link)
    {
        link.Handled = true;

        if (LinkCommand.CanExecute(link.Url))
        {
            LinkCommand.Execute(link.Url);
        }
    }
}
