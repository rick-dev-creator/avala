using Avala.Agents.Contracts;
using Avala.Jobs.Catalog;
using Avala.Jobs.Contracts;
using Avala.Jobs.Delivery;
using Avala.Jobs.Holding;
using Avala.Jobs.JobFiles;
using Avala.Jobs.Launching;
using Avala.Jobs.Ledger;
using Avala.Jobs.Recovery;
using Avala.Jobs.Review;
using Avala.Jobs.Storage;
using Avala.Jobs.Submission;
using Avala.Jobs.TurnChecks;
using Avala.Sdk;
using Avala.Sdk.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using JobAnnouncement = Avala.Jobs.Contracts.JobSubmitted;

namespace Avala.Jobs.UI;

public sealed class JobsPlugin : IPlugin
{
    public PluginInfo Info { get; } = new("avala.jobs", "Jobs");

    public void Register(IPluginRegistrar registrar)
    {
        registrar.Services.TryAddSingleton(TimeProvider.System);
        registrar.Services
            .AddSingleton<SqliteJobStore>()
            .AddForwarded<IJobStore, SqliteJobStore>()
            .AddForwarded<IStartupTask, SqliteJobStore>()
            .AddSingleton<IRepositoryDefaults, JobFileReader>()
            .AddSingleton<JobLedger>()
            .AddSingleton<JobQueues>()
            .AddSingleton<ConnectionChooser>()
            .AddSingleton<IConnectionPreview, ConnectionPreviewer>()
            .AddSingleton<WorkspacePlanner>()
            .AddSingleton<JobMessenger>()
            .AddSingleton<DeferredJobs>()
            .AddSingleton<JobLauncher>()
            .AddSingleton<ResumeJob>()
            .AddSingleton<CompletionGates>()
            .AddSingleton<EvaluateTurn>()
            .AddSingleton<RecoverJob>()
            .AddSingleton<SubmitJob>()
            .AddSingleton<HoldJob>()
            .AddSingleton<IApprovalStrategy, KeepStrategy>()
            .AddSingleton<IApprovalStrategy, MergeStrategy>()
            .AddSingleton<Approvals>()
            .AddSingleton<ReviewJob>()
            .AddSingleton<IJobs, JobsEntry>()
            .AddSingleton<IJobCatalog, JobCatalog>()
            .AddSingleton<IHandle<JobAnnouncement>, PrepareJob>()
            .AddSingleton<CheckTurn>()
            .AddForwarded<IHandle<TurnFinished>, CheckTurn>()
            .AddForwarded<IHandle<SessionEnded>, CheckTurn>()
            .AddForwarded<IHandle<SessionResumable>, CheckTurn>()
            .AddSingleton<IStartupTask, JobRecovery>();
    }
}
