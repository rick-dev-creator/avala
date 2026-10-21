using Avala.Components.Graphs;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Avala.Components.UI.Graphs;

internal sealed class AgentTreeStrokes
{
    public const int Steps = 20;

    public static readonly TimeSpan Stream = TimeSpan.FromMilliseconds(900);

    public static readonly TimeSpan Breath = TimeSpan.FromMilliseconds(2800);

    private const double Thickness = 1.2;

    private const double Dash = 4;

    private const double Gap = 6;

    private const double Faintest = 0.55;

    private const int Shades = 40;

    private static readonly int Kinds = Enum.GetValues<EdgeKind>().Length;

    private readonly ImmutablePen?[] flowing = new ImmutablePen?[Steps];
    private readonly ImmutablePen?[] still = new ImmutablePen?[Kinds];
    private TimeSpan? began;
    private int shade = Shades;

    public int Phase { get; private set; }

    public double Breathing { get; private set; } = 1;

    public void Reset()
    {
        Array.Clear(flowing);
        Array.Clear(still);
    }

    public void Rest()
    {
        began = null;
        (Phase, shade, Breathing) = (0, Shades, 1);
    }

    public bool Step(TimeSpan now)
    {
        began ??= now;
        var elapsed = now - began.Value;
        var phase = (int)(elapsed.Ticks % Stream.Ticks * Steps / Stream.Ticks);
        var wave = 0.5 + (0.5 * Math.Cos(2 * Math.PI * (elapsed.Ticks % Breath.Ticks) / Breath.Ticks));
        var next = (int)Math.Round((Faintest + ((1 - Faintest) * wave)) * Shades);
        var changed = phase != Phase || next != shade;
        (Phase, shade, Breathing) = (phase, next, (double)next / Shades);

        return changed;
    }

    public IPen For(Panel panel, EdgeKind kind) =>
        kind == EdgeKind.Flowing
            ? flowing[Phase] ??= new ImmutablePen(EdgeBrushes.GetFlowing(panel).ToImmutable(), Thickness, new ImmutableDashStyle([Dash / Thickness, Gap / Thickness], -Phase * (Dash + Gap) / Steps / Thickness))
            : still[(int)kind] ??= kind switch
            {
                EdgeKind.Attention => new ImmutablePen(EdgeBrushes.GetAttention(panel).ToImmutable(), Thickness),
                EdgeKind.Waiting => new ImmutablePen(EdgeBrushes.GetWaiting(panel).ToImmutable(), Thickness, new ImmutableDashStyle([2 / Thickness, 4 / Thickness], 0)),
                EdgeKind.Failure => new ImmutablePen(EdgeBrushes.GetFailure(panel).ToImmutable(), Thickness),
                _ => new ImmutablePen(EdgeBrushes.GetQuiet(panel).ToImmutable(), Thickness),
            };
}
