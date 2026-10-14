using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Avala.Components.UI.Markdown;

public sealed class CodeFile : Panel
{
    public static readonly RoutedEvent<RoutedEventArgs> MarkedEvent =
        RoutedEvent.Register<CodeFile, RoutedEventArgs>("Marked", RoutingStrategies.Bubble);

    public static readonly StyledProperty<string> CodeProperty =
        AvaloniaProperty.Register<CodeFile, string>(nameof(Code), string.Empty);

    public static readonly StyledProperty<string> LanguageProperty =
        AvaloniaProperty.Register<CodeFile, string>(nameof(Language), string.Empty);

    public static readonly StyledProperty<int> LineProperty =
        AvaloniaProperty.Register<CodeFile, int>(nameof(Line));

    private const double CodeLineHeight = 19;

    private readonly Border mark = new() { Name = "Mark", VerticalAlignment = VerticalAlignment.Top, IsVisible = false, IsHitTestVisible = false };

    public CodeFile()
    {
        mark.Bind(Border.BackgroundProperty, mark.GetResourceObservable("AccentSubtleBrush"));
        mark.Bind(Border.CornerRadiusProperty, mark.GetResourceObservable("RadiusButton"));
        Children.Add(mark);
    }

    public string Code
    {
        get => GetValue(CodeProperty);
        set => SetValue(CodeProperty, value);
    }

    public string Language
    {
        get => GetValue(LanguageProperty);
        set => SetValue(LanguageProperty, value);
    }

    public int Line
    {
        get => GetValue(LineProperty);
        set => SetValue(LineProperty, value);
    }

    public bool IsMarked => mark.IsVisible;

    public string ShownCode => Children.OfType<CodeText>().Select(code => code.Text ?? string.Empty).FirstOrDefault(string.Empty);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == CodeProperty || change.Property == LanguageProperty)
        {
            Show();
        }
        else if (change.Property == LineProperty)
        {
            Dispatcher.UIThread.Post(Reveal, DispatcherPriority.Background);
        }
    }

    private void Show()
    {
        foreach (var old in Children.OfType<CodeText>().ToList())
        {
            Children.Remove(old);
        }

        mark.IsVisible = false;

        if (Code.Length == 0)
        {
            return;
        }

        var code = new CodeText(Code, Language) { TextWrapping = TextWrapping.NoWrap, LineHeight = CodeLineHeight };
        code.Bind(TextBlock.FontFamilyProperty, code.GetResourceObservable("MonoFont"));
        code.Bind(TextBlock.FontSizeProperty, code.GetResourceObservable("FontSizeMono"));
        Children.Add(code);
        Dispatcher.UIThread.Post(Reveal, DispatcherPriority.Background);
    }

    private void Reveal()
    {
        if (Children.OfType<CodeText>().FirstOrDefault() is not { } code || Line <= 0 || code.TextLayout.TextLines.Count == 0)
        {
            mark.IsVisible = false;

            return;
        }

        var top = code.TextLayout.HitTestTextPosition(code.StartOf(Line)).Y + code.Padding.Top;
        mark.Height = CodeLineHeight;
        mark.Margin = new Thickness(-8, top, -8, 0);
        mark.IsVisible = true;
        RaiseEvent(new RoutedEventArgs(MarkedEvent));

        if (this.FindAncestorOfType<ScrollViewer>() is { } scroller)
        {
            scroller.Offset = new Vector(scroller.Offset.X, Math.Max(0, top - (scroller.Viewport.Height / 3)));
        }
    }
}
