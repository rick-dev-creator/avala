using Avala.Agents.Contracts;
using Avala.Jobs.Contracts;
using Avala.Jobs.Holding;
using Avala.Jobs.JobList;
using Avala.Jobs.Launching;
using Avala.Jobs.Ledger;
using Avala.Jobs.Recovery;
using Avala.Jobs.Storage;
using Avala.Jobs.Submission;
using Avala.Jobs.TurnChecks;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Sdk.UI;
using Microsoft.Extensions.DependencyInjection;
using JobAnnouncement = Avala.Jobs.Contracts.JobSubmitted;

namespace Avala.Jobs.UI;

public sealed class JobsPlugin : IPlugin, IViewContributor
{
    public PluginInfo Info { get; } = new("avala.jobs", "Jobs");

    public void Register(IPluginRegistrar registrar) =>
        registrar.Services
            .AddSingleton<IJobStore, SqliteJobStore>()
            .AddSingleton<JobLedger>()
            .AddSingleton<JobQueues>()
            .AddSingleton<JobLauncher>()
            .AddSingleton<CompletionGates>()
            .AddSingleton<EvaluateTurn>()
            .AddSingleton<SubmitJob>()
            .AddSingleton<HoldJob>()
            .AddSingleton<IJobs, JobsEntry>()
            .AddSingleton<IHandle<JobAnnouncement>, PrepareJob>()
            .AddSingleton<CheckTurn>()
            .AddSingleton<IHandle<TurnFinished>>(services => services.GetRequiredService<CheckTurn>())
            .AddSingleton<IHandle<SessionEnded>>(services => services.GetRequiredService<CheckTurn>())
            .AddSingleton<IHandle<SessionResumable>>(services => services.GetRequiredService<CheckTurn>())
            .AddSingleton<IStartupTask, JobRecovery>()
            .AddSingleton<IPage, JobsViewModel>();

    public void RegisterViews(IViewRegistrar views) =>
        views.Register<JobsViewModel, JobsView>();
}
