using Avala.Agents.Contracts;
using Avala.Budgets.Contracts;
using Avala.Canvas.Contracts;
using Avala.Delegation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Permissions.Contracts;
using Avala.Resources.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Sdk.UI;
using Avala.Supervision.Contracts;
using Avala.Verification.Contracts;
using Avala.Workbench.Board;
using Avala.Workbench.Cards;
using Avala.Workbench.Conversation;
using Avala.Workbench.Fleet;
using Avala.Workbench.Following;
using Avala.Workbench.Machine;
using Avala.Workbench.Navigation;
using Avala.Workbench.NewJob;
using Avala.Workbench.Overview;
using Avala.Workbench.Replies;
using Avala.Workbench.RepositoryRules;
using Avala.Workbench.Resources;
using Avala.Workbench.Settings;
using Avala.Workbench.Sidebar;
using Avala.Workbench.Spending;
using Avala.Workbench.Steering;
using Avala.Workbench.Submitting;
using Avala.Workbench.Upkeep;
using Avala.Workbench.Usage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Avala.Workbench.UI;

public sealed class WorkbenchPlugin : IPlugin, IViewContributor
{
    public PluginInfo Info { get; } = new("avala.workbench", "Workbench");

    public void Register(IPluginRegistrar registrar)
    {
        registrar.Services.TryAddSingleton(TimeProvider.System);
        registrar.Services
            .AddSingleton<JobBoard>()
            .AddSingleton<BoardKeeper>()
            .AddSingleton<IHandle<StartupCompleted>>(Keeper)
            .AddSingleton<IHandle<JobSubmitted>>(Keeper)
            .AddSingleton<IHandle<JobProgressed>>(Keeper)
            .AddSingleton<IHandle<JobHeld>>(Keeper)
            .AddSingleton<IHandle<JobApproved>>(Keeper)
            .AddSingleton<IHandle<JobSessionStarted>>(Keeper)
            .AddSingleton<IHandle<AgentActivity>>(Keeper)
            .AddSingleton<IHandle<CanvasUpdated>>(Keeper)
            .AddSingleton<IHandle<PermissionDecided>>(Keeper)
            .AddSingleton<IHandle<FormDecided>>(Keeper)
            .AddSingleton<IHandle<AttemptVerified>>(Keeper)
            .AddSingleton<JobSteering>()
            .AddSingleton<HumanReplies>()
            .AddSingleton<Conversations>()
            .AddSingleton<SidebarViewModel>()
            .AddSingleton<WorkbenchViewModel>()
            .AddSingleton<IPage>(services => services.GetRequiredService<WorkbenchViewModel>());
        RegisterGlobalPages(registrar.Services);
    }

    public void RegisterViews(IViewRegistrar views)
    {
        views.Register<WorkbenchViewModel, WorkbenchView>();
        views.Register<SidebarViewModel, SidebarView>();
        views.Register<JobRowViewModel, JobRowView>();
        views.Register<ConversationViewModel, ConversationView>();
        views.Register<ComposerViewModel, ComposerView>();
        views.Register<PromptViewModel, PromptView>();
        views.Register<RestartViewModel, RestartView>();
        views.Register<MessageViewModel, MessageView>();
        views.Register<ReasoningViewModel, ReasoningView>();
        views.Register<ToolViewModel, ToolView>();
        views.Register<PlanViewModel, PlanView>();
        views.Register<CanvasViewModel, CanvasView>();
        views.Register<TurnEndViewModel, TurnEndView>();
        views.Register<PermissionCardViewModel, PermissionCardView>();
        views.Register<FormCardViewModel, FormCardView>();
        views.Register<FormFieldViewModel, FormFieldView>();
        views.Register<FormChoiceViewModel, FormChoiceView>();
        RegisterGlobalViews(views);
    }

    private static void RegisterGlobalPages(IServiceCollection services) =>
        services
            .AddSingleton<Pulse>()
            .AddSingleton<IHandle<UsageRecorded>>(Get<Pulse>)
            .AddSingleton<IHandle<ResourcesSampled>>(Get<Pulse>)
            .AddSingleton<IHandle<OrphansFound>>(Get<Pulse>)
            .AddSingleton<IHandle<OrphansReaped>>(Get<Pulse>)
            .AddSingleton<IHandle<WorktreeReclaimed>>(Get<Pulse>)
            .AddSingleton<IHandle<WorktreesReconciled>>(Get<Pulse>)
            .AddSingleton<IHandle<ChildDelegated>>(Get<Pulse>)
            .AddSingleton<IHandle<DelegationRefused>>(Get<Pulse>)
            .AddSingleton<IHandle<ChildReported>>(Get<Pulse>)
            .AddSingleton<IHandle<BudgetCarved>>(Get<Pulse>)
            .AddSingleton<IHandle<BudgetIntervened>>(Get<Pulse>)
            .AddSingleton<IHandle<SupervisorIntervened>>(Get<Pulse>)
            .AddSingleton<SessionBook>()
            .AddSingleton<IHandle<SessionOpened>>(Get<SessionBook>)
            .AddSingleton<IHandle<JobSessionStarted>>(Get<SessionBook>)
            .AddSingleton<Leftovers>()
            .AddSingleton<IHandle<WorktreesReconciled>>(Get<Leftovers>)
            .AddTransient<LiveFeed>()
            .AddSingleton<JobSpending>()
            .AddSingleton<FleetReader>()
            .AddSingleton<DelegationReader>()
            .AddSingleton<UsageReader>()
            .AddSingleton<UsageWindows>()
            .AddSingleton<RulesReader>()
            .AddSingleton<MachineSettings>()
            .AddSingleton<SettingsFiles>()
            .AddSingleton<ResourceReader>()
            .AddSingleton<Housekeeping>()
            .AddSingleton<JobLaunch>()
            .AddSingleton<ConnectionsViewModel>()
            .AddSingleton<DelegationViewModel>()
            .AddSingleton<RepositorySettingsViewModel>()
            .AddSingleton<MachineSettingsViewModel>()
            .AddSingleton<ResourceIndicatorViewModel>()
            .AddSingleton<NewJobViewModel>()
            .AddSingleton<OverviewViewModel>()
            .AddSingleton<UsageViewModel>()
            .AddSingleton<ResourcesViewModel>()
            .AddSingleton<SettingsViewModel>()
            .AddSingleton<IPage>(Get<NewJobViewModel>)
            .AddSingleton<IPage>(Get<OverviewViewModel>)
            .AddSingleton<IPage>(Get<UsageViewModel>)
            .AddSingleton<IPage>(Get<ResourcesViewModel>)
            .AddSingleton<IPage>(Get<SettingsViewModel>);

    private static void RegisterGlobalViews(IViewRegistrar views)
    {
        views.Register<OverviewViewModel, OverviewView>();
        views.Register<ConnectionsViewModel, ConnectionsView>();
        views.Register<ConnectionCardViewModel, ConnectionCardView>();
        views.Register<AgentViewModel, AgentView>();
        views.Register<LimitViewModel, LimitView>();
        views.Register<DelegationViewModel, DelegationView>();
        views.Register<OrchestratorViewModel, OrchestratorView>();
        views.Register<DelegationNodeViewModel, DelegationNodeView>();
        views.Register<DelegationRefusalViewModel, DelegationRefusalView>();
        views.Register<UsageViewModel, UsageView>();
        views.Register<ConnectionMeterViewModel, ConnectionMeterView>();
        views.Register<UsageWindowViewModel, UsageWindowView>();
        views.Register<JobMeterViewModel, JobMeterView>();
        views.Register<InterventionViewModel, InterventionView>();
        views.Register<SettingsViewModel, SettingsView>();
        views.Register<RepositorySettingsViewModel, RepositorySettingsView>();
        views.Register<MachineSettingsViewModel, MachineSettingsView>();
        views.Register<RuleFileViewModel, RuleFileView>();
        views.Register<RuleViewModel, RuleView>();
        views.Register<CapsViewModel, CapsView>();
        views.Register<CheckViewModel, CheckView>();
        views.Register<JobSectionViewModel, JobSectionView>();
        views.Register<MachineConnectionViewModel, MachineConnectionView>();
        views.Register<ResourcesViewModel, ResourcesView>();
        views.Register<AgentTreeViewModel, AgentTreeView>();
        views.Register<OrphanViewModel, OrphanView>();
        views.Register<StaleWorktreeViewModel, StaleWorktreeView>();
        views.Register<ResourceIndicatorViewModel, ResourceIndicatorView>();
        views.Register<NewJobViewModel, NewJobView>();
    }

    private static T Get<T>(IServiceProvider services)
        where T : notnull =>
        services.GetRequiredService<T>();

    private static BoardKeeper Keeper(IServiceProvider services) => services.GetRequiredService<BoardKeeper>();
}
