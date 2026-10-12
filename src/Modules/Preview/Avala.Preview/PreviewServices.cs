using Avala.Jobs.Contracts;
using Avala.Preview.Contracts;
using Avala.Preview.Detection;
using Avala.Preview.Navigation;
using Avala.Preview.Pages;
using Avala.Preview.Pane;
using Avala.Preview.Projects;
using Avala.Preview.Running;
using Avala.Resources.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Avala.Preview;

internal static class PreviewServices
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddPreview()
        {
            services.TryAddSingleton(TimeProvider.System);

            return services
                .AddSingleton<PreviewBook>()
                .AddSingleton<RunBook>()
                .AddSingleton<PreviewDetector>()
                .AddForwarded<IHandle<ResourcesSampled>, PreviewDetector>()
                .AddForwarded<IHandle<JobProgressed>, PreviewDetector>()
                .AddSingleton<IJobWorktrees, JobWorktrees>()
                .AddSingleton<IProjectLaunches, ProjectLaunches>()
                .AddSingleton<PageServers>()
                .AddForwarded<IPageServers, PageServers>()
                .AddSingleton<ScriptRuns>()
                .AddSingleton<PageRuns>()
                .AddSingleton<PreviewRunner>()
                .AddSingleton<Previews>()
                .AddForwarded<IPreviews, Previews>()
                .AddForwarded<IHandle<JobProgressed>, Previews>()
                .AddTransient<PreviewFeed>()
                .AddTransient<PreviewDesk>()
                .AddSingleton<PreviewPaneViewModel>()
                .AddSingleton<RunScriptViewModel>();
        }
    }
}
