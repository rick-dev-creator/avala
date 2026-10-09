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
            .AddSingleton<ITaskLedger, SqliteTaskLedger>()
            .AddSingleton<IBacklogFile, BacklogFileReader>()
            .AddSingleton<IAutopilotRules, AutopilotRulesReader>()
            .AddSingleton<IJobSource, BacklogSource>()
            .AddSingleton<IJobSource, FollowUpSource>()
            .AddSingleton<IJobSource, RecurringSource>()
            .AddSingleton<TaskSources>()
            .AddSingleton<JobWork>()
            .AddSingleton<EvidenceGatherer>()
            .AddSingleton<AutoApprover>()
            .AddSingleton<LoopGauges>()
            .AddSingleton<LoopSteps>()
            .AddSingleton<LoopBook>()
            .AddSingleton<LoopJournal>()
            .AddSingleton<LoopRegistry>()
            .AddSingleton<IAutopilot>(services => services.GetRequiredService<LoopRegistry>())
            .AddSingleton<LoopFeed>()
            .AddSingleton<IHandle<JobProgressed>>(services => services.GetRequiredService<LoopFeed>())
            .AddSingleton<IHandle<JobHeld>>(services => services.GetRequiredService<LoopFeed>())
            .AddSingleton<IHandle<PermissionDecided>>(services => services.GetRequiredService<LoopFeed>())
            .AddSingleton<IHandle<FormDecided>>(services => services.GetRequiredService<LoopFeed>())
            .AddSingleton<FollowUpPolicy>()
            .AddSingleton<FollowUpDesk>()
            .AddSingleton<IHandle<SessionOpened>>(services => services.GetRequiredService<FollowUpDesk>())
            .AddSingleton<IHandle<JobSessionStarted>>(services => services.GetRequiredService<FollowUpDesk>())
            .AddSingleton<IHandle<AgentActivity>>(services => services.GetRequiredService<FollowUpDesk>());
    }
}
