using Avala.Sdk;
using Mermaider;
using Mermaider.Models;

namespace Avala.Mermaid.Translating;

internal sealed record MermaidPalette(string Background, string Foreground, string Accent, string Muted)
{
    public static MermaidPalette Dark { get; } = new("#17181C", "#EDEDEF", "#8DA2FB", "#A3A3AD");
}

internal sealed record MermaidBudget(TimeProvider Clock, TimeSpan Deadline)
{
    public static MermaidBudget Standard(TimeProvider clock) => new(clock, TimeSpan.FromSeconds(5));
}

internal enum MermaidFailure
{
    NotADiagram,
    OutOfTime,
}

internal static class MermaidSvg
{
    public static Result<string, MermaidFailure> Draw(string source, MermaidPalette palette, MermaidBudget budget)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return MermaidFailure.NotADiagram;
        }

        try
        {
            return CssVariables.Flatten(MermaidRenderer.RenderSvg(source, Options(palette, budget))).ToResult(MermaidFailure.NotADiagram);
        }
        catch (Exception exception) when (exception is OperationCanceledException or MermaidResourceLimitException { LimitName: nameof(ResourceLimits.RenderDeadline) })
        {
            return MermaidFailure.OutOfTime;
        }
        catch (Exception exception) when (exception is MermaidParseException or MermaidRenderException or MermaidSvgException or ArgumentException or InvalidOperationException)
        {
            return MermaidFailure.NotADiagram;
        }
    }

    private static RenderOptions Options(MermaidPalette palette, MermaidBudget budget) => new()
    {
        Bg = palette.Background,
        Fg = palette.Foreground,
        Accent = palette.Accent,
        Muted = palette.Muted,
        Font = "Inter",
        MonoFont = "JetBrains Mono",
        Transparent = true,
        Elevation = 0,
        Padding = 16,
        Limits = ResourceLimits.Default with { RenderDeadline = budget.Deadline, TimeProvider = budget.Clock },
    };
}
