using Avala.Components.UI.Canvases;
using Avala.Mermaid.Offer;
using Avala.Mermaid.UI.Drawing;
using Avala.Sdk;
using Avala.Sdk.UI;

namespace Avala.Mermaid.UI;

public sealed class MermaidPlugin : IPlugin, IViewContributor
{
    public PluginInfo Info { get; } = new("avala.mermaid", "Mermaid canvases");

    public void Register(IPluginRegistrar registrar) => registrar.Services.AddMermaidFormat();

    public void RegisterViews(IViewRegistrar views) => views.AddCanvasRenderer(new MermaidRenderer());
}
