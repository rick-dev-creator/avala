using Avala.Components.UI.Canvases;
using Avala.Mermaid.Offer;
using Avala.Mermaid.Translating;
using Avala.Sdk;
using Avalonia.Controls;

namespace Avala.Mermaid.UI.Drawing;

internal sealed class MermaidRenderer : ICanvasRenderer
{
    public string MediaType => MermaidFormat.Mermaid.MediaType;

    public Option<Control> Render(string content) =>
        MermaidSvg.Draw(content, MermaidPalette.Dark).Map<Control>(_ => new MermaidDrawing(content) { Name = "Mermaid" });
}
