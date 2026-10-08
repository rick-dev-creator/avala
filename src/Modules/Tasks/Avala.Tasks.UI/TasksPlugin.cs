using Avala.Sdk;
using Avala.Sdk.UI;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Tasks.UI;

public sealed class TasksPlugin : IPlugin, IViewContributor
{
    public PluginInfo Info { get; } = new("avala.tasks", "Tasks");

    public void Register(IPluginRegistrar registrar) =>
        registrar.Services.AddSingleton<IPage, TasksViewModel>();

    public void RegisterViews(IViewRegistrar views) =>
        views.Register<TasksViewModel, TasksView>();
}
