using Avala.Components.UI;
using Avala.Components.UI.Canvases;
using Avala.Rendering.Offer;
using Avala.Rendering.Sanitizing;
using Avala.Sdk;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Svg.Skia;
using Avalonia.VisualTree;

namespace Avala.Rendering.UI.Svg;

internal sealed class SvgRenderer(long budget) : ICanvasRenderer
{
    public const long Budget = 64L * 1024 * 1024;

    private readonly Dictionary<string, LinkedListNode<(string Content, SvgModel Model)>> models = [];
    private readonly LinkedList<(string Content, SvgModel Model)> recent = [];

    public SvgRenderer()
        : this(Budget)
    {
    }

    public string MediaType => RenderedFormats.Svg.MediaType;

    public IReadOnlyList<SvgModel> Kept => [.. recent.Select(entry => entry.Model)];

    public Option<Control> Render(string content) =>
        Load(content).Map(Show);

    private Option<SvgModel> Load(string content)
    {
        if (models.TryGetValue(content, out var known))
        {
            recent.Remove(known);
            recent.AddFirst(known);

            return known.Value.Model;
        }

        return SvgSanitizer.Sanitize(content).Map(sanitized => Keep(content, new SvgModel(sanitized)));
    }

    private SvgModel Keep(string content, SvgModel model)
    {
        models[content] = recent.AddFirst((content, model));
        var bytes = recent.Sum(entry => entry.Model.Bytes);

        while (bytes > budget && recent.Last is { } oldest && oldest != recent.First)
        {
            bytes -= oldest.Value.Model.Bytes;
            recent.RemoveLast();
            models.Remove(oldest.Value.Content);
            oldest.Value.Model.Evict();
        }

        return model;
    }

    private static Control Show(SvgModel model)
    {
        var drawing = model.Sanitized.Timeline.Match<Control>(timeline => new AnimatedSvg(model, timeline), () => Still(model));

        if (model.Sanitized.Blocked == 0)
        {
            return drawing;
        }

        var panel = new StackPanel { Children = { drawing, BlockedNote(model.Sanitized.Blocked) } };
        panel.Bind(StackPanel.SpacingProperty, panel.GetResourceObservable("Space2"));

        return panel;
    }

    private static Viewbox Still(SvgModel model)
    {
        var image = new Image { Name = "Svg", Stretch = Stretch.Fill };
        _ = model.Sanitized.Area.Map(area => (image.Width, image.Height) = (area.Width, area.Height));
        CanvasPresenter.SetIsDrawing(image, true);
        image.AttachedToVisualTree += (_, _) => _ = ShowAsync(image, model.ClaimAsync());
        image.DetachedFromVisualTree += (_, _) =>
        {
            image.Source = null;
            model.Release();
        };

        return SvgModel.Fit(image);
    }

    private static async Task ShowAsync(Image image, Task<Option<SvgSource>> loading)
    {
        var loaded = await loading;

        if (image.IsAttachedToVisualTree())
        {
            _ = loaded.Map(source => image.Source = new SvgImage { Source = source });
        }

        if (CanvasPresenter.GetIsDrawing(image))
        {
            CanvasPresenter.Drawn(image);
        }
    }

    private static TextBlock BlockedNote(int count) => new()
    {
        Name = "Blocked",
        Text = count == 1
            ? "1 script or external reference was blocked."
            : $"{count} scripts or external references were blocked.",
        Classes = { "caption", "tertiary" },
    };
}
