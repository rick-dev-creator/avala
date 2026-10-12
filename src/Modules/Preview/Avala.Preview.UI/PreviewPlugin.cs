using Avala.Preview.Pane;
using Avala.Preview.Surfaces;
using Avala.Preview.UI.Surfaces;
using Avala.Sdk;
using Avala.Sdk.UI;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Preview.UI;

public sealed class PreviewPlugin : IPlugin, IViewContributor
{
    public PluginInfo Info { get; } = new("avala.preview", "Preview");

    public void Register(IPluginRegistrar registrar) =>
        registrar.Services
            .AddPreview()
            .AddSingleton<IWebSurfaces, NativeSurfaces>();

    public void RegisterViews(IViewRegistrar views)
    {
        views.Register<IPreviewPaneViewModel, PreviewPaneView>();
        views.Register<IRunningChipViewModel, RunningChipView>();
        views.Register<IRunScriptViewModel, RunScriptView>();
        views.Register(new NativeSurfaceTemplate());
    }
}
