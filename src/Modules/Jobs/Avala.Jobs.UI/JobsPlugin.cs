using Avala.Jobs.ViewModels;
using Avala.Sdk;
using Avala.Sdk.UI;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Jobs.UI;

public sealed class JobsPlugin : IPlugin, IViewContributor
{
    public PluginInfo Info { get; } = new("avala.jobs", "Jobs");

    public void Register(IPluginRegistrar registrar) =>
        registrar.Services.AddSingleton<IPage, JobsViewModel>();

    public void RegisterViews(IViewRegistrar views) =>
        views.Register<JobsViewModel, JobsView>();
}
