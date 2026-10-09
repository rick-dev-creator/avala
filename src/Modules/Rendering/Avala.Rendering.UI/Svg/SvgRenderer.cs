using Avala.Components.Canvases;
using Avala.Components.UI.Canvases;
using Avala.Rendering.Sanitizing;
using Avala.Sdk;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Svg.Skia;

namespace Avala.Rendering.UI.Svg;

internal sealed class SvgRenderer : ICanvasRenderer
{
    public bool Renders(string mediaType) => mediaType == CanvasMediaTypes.Svg;

    public Option<Control> Render(string content) =>
        SvgSanitizer.Sanitize(content).Bind(Draw);

    private static Option<Control> Draw(SanitizedSvg svg)
    {
        var source = SvgSource.LoadFromSvg(svg.Markup);

        if (source.Picture is null)
        {
            source.Dispose();

            return Option<Control>.None;
        }

        var drawing = new Image
        {
            Name = "Svg",
            Source = new SvgImage { Source = source },
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        if (svg.Blocked == 0)
        {
            return drawing;
        }

        var panel = new StackPanel { Children = { drawing, Blocked(svg.Blocked) } };
        panel.Bind(StackPanel.SpacingProperty, panel.GetResourceObservable("Space2"));

        return panel;
    }

    private static TextBlock Blocked(int count) => new()
    {
        Name = "Blocked",
        Text = count == 1
            ? "1 script or external reference was blocked."
            : $"{count} scripts or external references were blocked.",
        Classes = { "caption", "tertiary" },
    };
}
