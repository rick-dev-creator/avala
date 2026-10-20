using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Avala.Components.UI.Markdown;

public sealed class CodeSnippet : Decorator
{
    public static readonly StyledProperty<string> CodeProperty =
        AvaloniaProperty.Register<CodeSnippet, string>(nameof(Code), string.Empty);

    public static readonly StyledProperty<string> LanguageProperty =
        AvaloniaProperty.Register<CodeSnippet, string>(nameof(Language), string.Empty);

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

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == CodeProperty || change.Property == LanguageProperty)
        {
            Child = Code.Length == 0 ? null : new CodeText(Code, Language) { TextWrapping = TextWrapping.Wrap };
        }
    }
}
