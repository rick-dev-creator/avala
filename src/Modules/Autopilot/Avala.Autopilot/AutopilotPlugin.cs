using Avala.Agents.Contracts;
using Avala.Autopilot.Approving;
using Avala.Autopilot.Contracts;
using Avala.Autopilot.FollowUps;
using Avala.Autopilot.Looping;
using Avala.Autopilot.RepositoryFiles;
using Avala.Autopilot.Sourcing;
using Avala.Autopilot.Storage;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Avala.Autopilot;

public sealed class AutopilotPlugin : IPlugin
{
    public PluginInfo Info { get; } = new("avala.autopilot", "Autopilot");

    public void Register(IPluginRegistrar registrar)
    {
        registrar.Services.TryAddSingleton(TimeProvider.System);
        registrar.Services
            .AddSingleton(FollowUpTool.Definition)
            .AddSingleton<SqliteTaskLedger>()
            .AddForwarded<ITaskLedger, SqliteTaskLedger>()
            .AddForwarded<IStartupTask, SqliteTaskLedger>()
            .AddSingleton<IBacklogFile, BacklogFileReader>()
            .AddSingleton<IAutopilotRules, AutopilotRulesReader>()
            .AddSingleton<IJobSource, BacklogSource>()
            .AddSingleton<IJobSource, FollowUpSource>()
            .AddSingleton<IJobSource, RecurringSource>()
            .AddSingleton<TaskSources>()
            .AddSingleton<JobWork>()
            .AddSingleton<EvidenceGatherer>()
            .AddSingleton<IRunEvidence, RunEvidenceQuery>()
            .AddSingleton<AutoApprover>()
            .AddSingleton<LoopGauges>()
            .AddSingleton<LoopSteps>()
            .AddSingleton<LoopBook>()
            .AddSingleton<LoopJournal>()
            .AddSingleton<LoopRegistry>()
            .AddForwarded<IAutopilot, LoopRegistry>()
            .AddSingleton<LoopFeed>()
            .AddForwarded<IHandle<JobProgressed>, LoopFeed>()
            .AddForwarded<IHandle<JobHeld>, LoopFeed>()
            .AddForwarded<IHandle<PermissionDecided>, LoopFeed>()
            .AddForwarded<IHandle<FormDecided>, LoopFeed>()
            .AddSingleton<FollowUpPolicy>()
            .AddSingleton<FollowUpDesk>()
            .AddForwarded<IHandle<SessionOpened>, FollowUpDesk>()
            .AddForwarded<IHandle<JobSessionStarted>, FollowUpDesk>()
            .AddForwarded<IHandle<AgentActivity>, FollowUpDesk>();
    }
}
