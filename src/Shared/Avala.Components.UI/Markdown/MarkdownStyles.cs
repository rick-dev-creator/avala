using Avalonia;
using Avalonia.Markup.Xaml.Styling;

namespace Avala.Components.UI.Markdown;

public sealed class MarkdownStyles
{
    public static readonly AttachedProperty<bool> AreSharedProperty =
        AvaloniaProperty.RegisterAttached<MarkdownStyles, StyledElement, bool>("AreShared", inherits: true);

    private static readonly Uri Base = new("avares://Avala.Components.UI/");

    static MarkdownStyles() => AreSharedProperty.Changed.AddClassHandler<StyledElement>((host, change) =>
    {
        if (change.Priority == Avalonia.Data.BindingPriority.LocalValue && GetAreShared(host))
        {
            host.Styles.Add(Include());
        }
    });

    private MarkdownStyles()
    {
    }

    public static bool GetAreShared(StyledElement element) => element.GetValue(AreSharedProperty);

    public static void SetAreShared(StyledElement element, bool value) => element.SetValue(AreSharedProperty, value);

    internal static StyleInclude Include() => new(Base) { Source = new Uri("Markdown/MarkdownStyles.axaml", UriKind.Relative) };
}
