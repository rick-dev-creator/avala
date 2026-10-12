using Avala.Components.UI.Canvases;
using Avala.Rendering.Offer;
using Avala.Rendering.Sanitizing;
using Avala.Sdk;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Svg.Skia;

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

        return new Drawn(source, svg.Blocked);
    }

    private static Control Show(Drawn svg)
    {
        var drawing = new Image
        {
            Name = "Svg",
            Source = new SvgImage { Source = svg.Source },
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        if (svg.Blocked == 0)
        {
            return drawing;
        }

        var panel = new StackPanel { Children = { drawing, BlockedNote(svg.Blocked) } };
        panel.Bind(StackPanel.SpacingProperty, panel.GetResourceObservable("Space2"));

        return panel;
    }

    private sealed record Drawn(SvgSource Source, int Blocked);

    private static TextBlock BlockedNote(int count) => new()
    {
        Name = "Blocked",
        Text = count == 1
            ? "1 script or external reference was blocked."
            : $"{count} scripts or external references were blocked.",
        Classes = { "caption", "tertiary" },
    };
}
