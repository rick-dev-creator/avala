using Avala.Preview.Pane;
using Avala.Sdk;
using Avala.Sdk.UI;

namespace Avala.Preview.UI;

public sealed class PreviewPlugin : IPlugin, IViewContributor
{
    public PluginInfo Info { get; } = new("avala.preview", "Preview");

    public void Register(IPluginRegistrar registrar) => registrar.Services.AddPreview();

    public void RegisterViews(IViewRegistrar views)
    {
        views.Register<IPreviewPaneViewModel, PreviewPaneView>();
        views.Register<IRunningChipViewModel, RunningChipView>();
        views.Register<IRunScriptViewModel, RunScriptView>();
    }
}
