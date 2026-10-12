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
    private int holders;
    private bool evicted;

    public SanitizedSvg Sanitized => sanitized;

    public long Bytes { get; private set; } = sanitized.Markup.Length * 2L;

    public bool IsLoaded => loaded is not null;

    public static Viewbox Fit(Control drawing) => new()
    {
        Name = "SvgFit",
        Stretch = Stretch.Uniform,
        HorizontalAlignment = HorizontalAlignment.Center,
        Child = drawing,
    };

    public Task<Option<SvgSource>> ClaimAsync()
    {
        holders++;

        return loaded ??= LoadAsync();
    }

    public void Release()
    {
        holders--;

        if (evicted && holders == 0)
        {
            Unload();
        }
    }

    public void Evict()
    {
        evicted = true;

        if (holders == 0)
        {
            Unload();
        }
    }

    private async Task<Option<SvgSource>> LoadAsync()
    {
        var source = await Task.Run(Load);
        Bytes = (sanitized.Markup.Length * 2L) + source.Match(found => found.Picture?.ApproximateBytesUsed ?? 0, () => 0);

        return source;
    }

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

    private void Unload()
    {
        if (loaded is { } dropped)
        {
            loaded = null;
            _ = DisposeAsync(dropped);
        }
    }

    private static async Task DisposeAsync(Task<Option<SvgSource>> dropped) =>
        _ = (await dropped).Map(found =>
        {
            found.Dispose();

            return true;
        });
}
