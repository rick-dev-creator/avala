using Avala.Rendering.Sanitizing;
using Avala.Sdk;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Svg.Skia;

namespace Avala.Rendering.UI.Svg;

internal sealed class SvgModel(SanitizedSvg sanitized)
{
    private Task<Option<SvgSource>>? loaded;

    public SanitizedSvg Sanitized => sanitized;

    public Task<Option<SvgSource>> Loaded => loaded ??= Task.Run(Load);

    public static Viewbox Fit(Control drawing) => new()
    {
        Name = "SvgFit",
        Stretch = Stretch.Uniform,
        HorizontalAlignment = HorizontalAlignment.Center,
        Child = drawing,
    };

    private Option<SvgSource> Load()
    {
        var source = SvgSource.LoadFromSvg(sanitized.Markup);

        if (source.Picture is not null)
        {
            return source;
        }

        source.Dispose();

        return Option<SvgSource>.None;
    }
}
