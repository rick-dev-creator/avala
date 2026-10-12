using Avala.Mermaid.Translating;
using Avala.Sdk;
using Avalonia.Threading;

namespace Avala.Mermaid.UI.Drawing;

internal sealed class MermaidDrawings(TimeProvider clock, long budget)
{
    public const long StandardBudget = 16L * 1024 * 1024;

    private readonly Dictionary<(string Source, MermaidPalette Palette), Task<Result<string, MermaidFailure>>> drawn = [];
    private readonly LinkedList<(string Source, MermaidPalette Palette, long Bytes)> recent = [];
    private readonly HashSet<string> failed = new(StringComparer.Ordinal);

    public MermaidDrawings(TimeProvider clock)
        : this(clock, StandardBudget)
    {
    }

    public long Budget => budget;

    public long CachedBytes { get; private set; }

    public bool HasFailed(string source)
    {
        Dispatcher.UIThread.VerifyAccess();

        return failed.Contains(source);
    }

    public Task<Result<string, MermaidFailure>> DrawAsync(string source, MermaidPalette palette)
    {
        Dispatcher.UIThread.VerifyAccess();
        var key = (source, palette);

        if (drawn.TryGetValue(key, out var known))
        {
            return known;
        }

        var drawing = RenderAsync(source, palette);
        drawn[key] = drawing;

        return drawing;
    }

    private async Task<Result<string, MermaidFailure>> RenderAsync(string source, MermaidPalette palette)
    {
        var svg = await Task.Run(() => MermaidSvg.Draw(source, palette, MermaidBudget.Standard(clock)));
        Remember(source, palette, svg);

        return svg;
    }

    private void Remember(string source, MermaidPalette palette, Result<string, MermaidFailure> svg)
    {
        var bytes = 2L * (source.Length + svg.Match(drawing => drawing.Length, _ => 0));

        if (svg.Match(_ => false, failure => failure == MermaidFailure.NotADiagram))
        {
            failed.Add(source);
        }

        recent.AddLast((source, palette, bytes));
        CachedBytes += bytes;

        while (CachedBytes > budget && recent.First is { } oldest)
        {
            recent.RemoveFirst();
            CachedBytes -= oldest.Value.Bytes;
            drawn.Remove((oldest.Value.Source, oldest.Value.Palette));
            failed.Remove(oldest.Value.Source);
        }
    }
}
