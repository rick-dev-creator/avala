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
    private const int Kept = 16;

    private readonly Dictionary<string, Drawn> drawn = [];
    private readonly Queue<string> order = [];

    public string MediaType => RenderedFormats.Svg.MediaType;

    public Option<Control> Render(string content) =>
        Load(content).Map(Show);

    private Option<Drawn> Load(string content)
    {
        if (drawn.TryGetValue(content, out var known))
        {
            return known;
        }

        var loaded = SvgSanitizer.Sanitize(content).Bind(Parse);
        _ = loaded.Map(found => Keep(content, found));

        return loaded;
    }

    private Drawn Keep(string content, Drawn found)
    {
        drawn[content] = found;
        order.Enqueue(content);

        if (order.Count > Kept)
        {
            drawn.Remove(order.Dequeue());
        }

        return found;
    }

    private static Option<Drawn> Parse(SanitizedSvg svg)
    {
        var source = SvgSource.LoadFromSvg(svg.Markup);

        if (source.Picture is null)
        {
            source.Dispose();

            return Option<Drawn>.None;
        }

        return new Drawn(source, svg);
    }

    private static Control Show(Drawn svg)
    {
        var drawing = svg.Sanitized.Timeline.Match(timeline => Animated(svg, timeline), () => Still(svg));

        if (svg.Sanitized.Blocked == 0)
        {
            return drawing;
        }

        var panel = new StackPanel { Children = { drawing, BlockedNote(svg.Sanitized.Blocked) } };
        panel.Bind(StackPanel.SpacingProperty, panel.GetResourceObservable("Space2"));

        return panel;
    }

    private static Control Animated(Drawn svg, SvgTimeline timeline) =>
        SvgService.FromSvg(svg.Sanitized.Markup) is { } document && new AnimatedSvg(document, timeline) is { Moves: true } animated
            ? animated
            : Still(svg);

    private static Image Still(Drawn svg) => new()
    {
        Name = "Svg",
        Source = new SvgImage { Source = svg.Source },
        Stretch = Stretch.Uniform,
        HorizontalAlignment = HorizontalAlignment.Center,
    };

    private sealed record Drawn(SvgSource Source, SanitizedSvg Sanitized);

    private static TextBlock BlockedNote(int count) => new()
    {
        Name = "Blocked",
        Text = count == 1
            ? "1 script or external reference was blocked."
            : $"{count} scripts or external references were blocked.",
        Classes = { "caption", "tertiary" },
    };
}
