using Avala.Canvas.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Mermaid.Offer;

internal static class MermaidFormat
{
    public static CanvasFormat Mermaid { get; } = new(
        "text/vnd.mermaid",
        "Mermaid",
        "Write a standard flowchart, sequence, state, class or entity-relationship diagram as Mermaid when its syntax fits; Avala draws it in the app's theme.");

    extension(IServiceCollection services)
    {
        public IServiceCollection AddMermaidFormat() => services.AddSingleton(Mermaid);
    }
}
