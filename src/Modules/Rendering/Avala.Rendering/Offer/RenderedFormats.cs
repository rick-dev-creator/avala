using Avala.Canvas.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Rendering.Offer;

internal static class RenderedFormats
{
    public static CanvasFormat Svg { get; } = new(
        "image/svg+xml",
        "SVG",
        "Draw every diagram, chart, flow, screen or design as SVG.");

    public static CanvasFormat Markdown { get; } = new(
        "text/markdown",
        "Markdown",
        "Write notes, tables, plans and reports as Markdown.");

    public static IReadOnlyList<CanvasFormat> All { get; } = [Svg, Markdown];

    extension(IServiceCollection services)
    {
        public IServiceCollection AddRenderedFormats()
        {
            foreach (var format in All)
            {
                services.AddSingleton(format);
            }

            return services;
        }
    }
}
