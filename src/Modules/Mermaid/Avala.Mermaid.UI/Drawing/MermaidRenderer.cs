using Avala.Components.UI.Canvases;
using Avala.Mermaid.Offer;
using Avala.Sdk;
using Avalonia.Controls;

namespace Avala.Mermaid.UI.Drawing;

internal sealed class MermaidRenderer(MermaidDrawings drawings) : ICanvasRenderer
{
    public string MediaType => MermaidFormat.Mermaid.MediaType;

    public Option<Control> Render(string content) =>
        string.IsNullOrWhiteSpace(content) || drawings.HasFailed(content)
            ? Option<Control>.None
            : new MermaidDrawing(content, drawings) { Name = "Mermaid" };
}
