using Avala.Components.UI;
using Avala.Components.UI.Canvases;
using Avala.Rendering.Offer;
using Avala.Rendering.Sanitizing;
using Avala.Sdk;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Svg.Skia;

namespace Avala.Rendering.UI.Svg;

internal sealed class SvgRenderer : ICanvasRenderer
{
    private const int Kept = 16;

    private readonly Dictionary<string, SvgModel> models = [];
    private readonly Queue<string> order = [];

    public string MediaType => RenderedFormats.Svg.MediaType;

    public Option<Control> Render(string content) =>
        Load(content).Map(Show);

    private Option<SvgModel> Load(string content)
    {
        if (models.TryGetValue(content, out var known))
        {
            return known;
        }

        return SvgSanitizer.Sanitize(content).Map(sanitized => Keep(content, new SvgModel(sanitized)));
    }

    private SvgModel Keep(string content, SvgModel model)
    {
        models[content] = model;
        order.Enqueue(content);

        if (order.Count > Kept)
        {
            models.Remove(order.Dequeue());
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
        _ = ShowAsync(image, model.Loaded);

        return SvgModel.Fit(image);
    }

    private static async Task ShowAsync(Image image, Task<Option<SvgSource>> loading)
    {
        _ = (await loading).Map(source => image.Source = new SvgImage { Source = source });
        CanvasPresenter.Drawn(image);
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
