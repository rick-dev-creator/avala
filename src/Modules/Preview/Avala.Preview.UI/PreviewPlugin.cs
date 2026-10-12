using Avala.Preview.Navigation;
using Avala.Preview.Pane;
using Avala.Preview.Contracts;
using Avala.Preview.UI.Surfaces;
using Avala.Sdk;
using Avala.Sdk.Regions;
using Avala.Sdk.UI;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Preview.UI;

public sealed class PreviewPlugin : IPlugin, IViewContributor
{
    public PluginInfo Info { get; } = new("avala.preview", "Preview");

    public void Register(IPluginRegistrar registrar)
    {
        NativeSurfaces.Prepare();
        registrar.Services
            .AddPreview()
            .AddSingleton<IWebSurfaces, NativeSurfaces>()
            .AddSingleton(provider => new RegionContribution(WorkbenchRegions.JobHeader, 0, Chip(provider, ChipPlacement.Header)))
            .AddSingleton(provider => new RegionContribution(WorkbenchRegions.Conversation, 0, Chip(provider, ChipPlacement.Conversation)));
        registrar
            .AddToRegion<RunScriptViewModel>(WorkbenchRegions.JobHeader, 10)
            .AddToRegion<PreviewPaneViewModel>(WorkbenchRegions.Preview, 0);
    }

    public void RegisterViews(IViewRegistrar views)
    {
        views.Register<IPreviewPaneViewModel, PreviewPaneView>();
        views.Register<IRunningChipViewModel, RunningChipView>();
        views.Register<IRunScriptViewModel, RunScriptView>();
        views.Register(new NativeSurfaceTemplate());
    }

    private static RunningChipViewModel Chip(IServiceProvider provider, ChipPlacement placement) =>
        new(provider.GetRequiredService<PreviewDesk>(), provider.GetRequiredService<IMessenger>(), placement);
}
