using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace Avala.Testing.UI;

public sealed record TextContrast(string Text, Color Foreground, Color Background, double Ratio);

public static class Legibility
{
    public static double Contrast(Color first, Color second)
    {
        var (lighter, darker) = (Luminance(first), Luminance(second)) is var (a, b) && a >= b ? (a, b) : (b, a);

        return (lighter + 0.05) / (darker + 0.05);
    }

    public static Color Over(Color top, Color bottom)
    {
        var alpha = top.A / 255.0;

        return Color.FromRgb(Blend(top.R, bottom.R, alpha), Blend(top.G, bottom.G, alpha), Blend(top.B, bottom.B, alpha));
    }

    public static Color Resource(string key, ThemeVariant variant) =>
        Application.Current!.TryGetResource(key, variant, out var value) switch
        {
            true when value is Color color => color,
            true when value is ISolidColorBrush brush => brush.Color,
            _ => throw new InvalidOperationException($"The theme has no color {key} for {variant}."),
        };

    public static IReadOnlyList<TextContrast> Texts(Window window, Color surface) =>
        [.. window.GetVisualDescendants().OfType<TextBlock>()
            .Where(text => text.IsEffectivelyVisible && text.IsEffectivelyEnabled && !string.IsNullOrWhiteSpace(text.Text) && text.Bounds.Width > 0)
            .Where(text => text.Foreground is ISolidColorBrush)
            .Select(text => Measure(text, surface))];

    private static TextContrast Measure(TextBlock text, Color surface)
    {
        var background = text.GetVisualAncestors().Reverse().Aggregate(surface, (behind, visual) => Fill(visual) is { } fill ? Over(fill, behind) : behind);
        var foreground = Over(((ISolidColorBrush)text.Foreground!).Color, background);

        return new TextContrast(text.Text!, foreground, background, Contrast(foreground, background));
    }

    private static Color? Fill(Visual visual) =>
        visual switch
        {
            Border { Background: ISolidColorBrush brush } => WithOpacity(brush),
            Panel { Background: ISolidColorBrush brush } => WithOpacity(brush),
            ContentPresenter { Background: ISolidColorBrush brush } => WithOpacity(brush),
            _ => null,
        };

    private static Color WithOpacity(ISolidColorBrush brush) =>
        Color.FromArgb((byte)Math.Round(brush.Color.A * brush.Opacity), brush.Color.R, brush.Color.G, brush.Color.B);

    private static byte Blend(byte top, byte bottom, double alpha) => (byte)Math.Round((top * alpha) + (bottom * (1 - alpha)));

    private static double Luminance(Color color) =>
        (0.2126 * Linear(color.R)) + (0.7152 * Linear(color.G)) + (0.0722 * Linear(color.B));

    private static double Linear(byte channel)
    {
        var value = channel / 255.0;

        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }
}
