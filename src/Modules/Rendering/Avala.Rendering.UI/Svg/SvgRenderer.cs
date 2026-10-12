using Avala.Components.UI.Canvases;
using Avala.Rendering.Animation;
using Avala.Rendering.Offer;
using Avala.Rendering.Sanitizing;
using Avala.Sdk;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Svg.Skia;
using Svg.Model.Services;

namespace Avala.Rendering.UI.Svg;

internal sealed class SvgRenderer : ICanvasRenderer
{
    public string MediaType => RenderedFormats.Svg.MediaType;

    public Option<Control> Render(string content) =>
        SvgSanitizer.Sanitize(content).Bind(Draw);

    private static Option<Control> Draw(SanitizedSvg svg) =>
        svg.Timeline.Match(timeline => Animated(svg.Markup, timeline), () => Still(svg.Markup))
            .Map(drawing => Captioned(drawing, svg.Blocked));

    private static Option<Control> Animated(string markup, SvgTimeline timeline)
    {
        var drawing = SvgService.FromSvg(markup) is { } document ? new AnimatedSvg(document, timeline) : null;

        return drawing is { Moves: true } ? drawing : Still(markup);
    }

    private static Option<Control> Still(string markup)
    {
        var source = SvgSource.LoadFromSvg(markup);

        if (source.Picture is null)
        {
            source.Dispose();

            return Option<Control>.None;
        }

        return new Image
        {
            Name = "Svg",
            Source = new SvgImage { Source = source },
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
    }

    private static Control Captioned(Control drawing, int blocked)
    {
        if (blocked == 0)
        {
            return drawing;
        }

        var panel = new StackPanel { Children = { drawing, Blocked(blocked) } };
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
