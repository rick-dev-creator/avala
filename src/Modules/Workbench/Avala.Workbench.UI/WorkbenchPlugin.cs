using Avala.Agents.Contracts;
using Avala.Budgets.Contracts;
using Avala.Canvas.Contracts;
using Avala.Delegation.Contracts;
using Avala.Handoffs.Contracts;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Permissions.Contracts;
using Avala.Resources.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Sdk.Regions;
using Avala.Sdk.UI;
using Avala.Sdk.Updates;
using Avala.Supervision.Contracts;
using Avala.Triggers.Contracts;
using Avala.Verification.Contracts;
using Avala.Workbench.Automation;
using Avala.Workbench.Board;
using Avala.Workbench.Cards;
using Avala.Workbench.ModelChoices;
using Avala.Workbench.Conversation;
using Avala.Workbench.Decisions;
using Avala.Workbench.Fleet;
using Avala.Workbench.Following;
using Avala.Workbench.Inspection;
using Avala.Workbench.Inspector;
using Avala.Workbench.Linking;
using Avala.Workbench.Machine;
using Avala.Workbench.Navigation;
using Avala.Workbench.NewJob;
using Avala.Workbench.Overview;
using Avala.Workbench.Presenting;
using Avala.Workbench.Replies;
using Avala.Workbench.RepositoryRules;
using Avala.Workbench.Resources;
using Avala.Workbench.Review;
using Avala.Workbench.Reviewing;
using Avala.Workbench.Settings;
using Avala.Workbench.Sidebar;
using Avala.Workbench.Spending;
using Avala.Workbench.Steering;
using Avala.Workbench.Submitting;
using Avala.Workbench.Triggers;
using Avala.Workbench.Updates;
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
            .AddSingleton<BoardJoiner>()
            .AddSingleton<BoardKeeper>()
            .AddForwarded<IHandle<StartupCompleted>, BoardKeeper>()
            .AddForwarded<IHandle<JobSubmitted>, BoardKeeper>()
            .AddForwarded<IHandle<JobProgressed>, BoardKeeper>()
            .AddForwarded<IHandle<JobHeld>, BoardKeeper>()
            .AddForwarded<IHandle<JobApproved>, BoardKeeper>()
            .AddForwarded<IHandle<SessionOpened>, BoardKeeper>()
            .AddForwarded<IHandle<JobSessionStarted>, BoardKeeper>()
            .AddForwarded<IHandle<ConnectionChosen>, BoardKeeper>()
            .AddForwarded<IHandle<AgentActivity>, BoardKeeper>()
            .AddForwarded<IHandle<CanvasUpdated>, BoardKeeper>()
            .AddForwarded<IHandle<PermissionDecided>, BoardKeeper>()
            .AddForwarded<IHandle<FormDecided>, BoardKeeper>()
            .AddForwarded<IHandle<AttemptVerified>, BoardKeeper>()
            .AddForwarded<IHandle<PermissionAnswered>, BoardKeeper>()
            .AddForwarded<IHandle<AutonomyApplied>, BoardKeeper>()
            .AddForwarded<IHandle<UsageRecorded>, BoardKeeper>()
            .AddForwarded<IHandle<BudgetIntervened>, BoardKeeper>()
            .AddForwarded<IHandle<BudgetCarved>, BoardKeeper>()
            .AddForwarded<IHandle<ChildDelegated>, BoardKeeper>()
            .AddForwarded<IHandle<ChildReported>, BoardKeeper>()
            .AddForwarded<IHandle<HandoffRecorded>, BoardKeeper>()
            .AddForwarded<IHandle<JobWaitsForReset>, BoardKeeper>()
            .AddTransient<BoardFeed>()
            .AddSingleton<JobFocus>()
            .AddSingleton<JobSteering>()
            .AddSingleton<QueuedMessages>()
            .AddForwarded<IHandle<JobProgressed>, QueuedMessages>()
            .AddSingleton<HumanReplies>()
            .AddSingleton<Links>()
            .AddSingleton<Conversations>()
            .AddSingleton<ReviewReader>()
            .AddSingleton<ReviewDesk>()
            .AddSingleton<Reviews>()
            .AddSingleton<JobScreens>()
            .AddSingleton<DecisionsViewModel>()
            .AddForwarded<IDecisionsViewModel, DecisionsViewModel>()
            .AddSingleton<SidebarViewModel>()
            .AddSingleton<ToolbarViewModel>()
            .AddSingleton<SettingsLink>()
            .AddSingleton<IFirstRunViewModel, FirstRunViewModel>()
            .AddSingleton<WorkbenchViewModel>()
            .AddForwarded<IPage, WorkbenchViewModel>();
        registrar.AddToRegion<SidebarViewModel>(ShellRegions.Sidebar, 0);
        registrar.AddToRegion<ToolbarViewModel>(ShellRegions.Toolbar, 0);
        RegisterInspector(registrar);
        RegisterGlobalPages(registrar.Services);
        registrar.AddToRegion<ResourceIndicatorViewModel>(ShellRegions.SidebarFooter, 0);
        registrar.AddToRegion<UpdateNoticeViewModel>(ShellRegions.SidebarFooter, 10);
    }

    public void RegisterViews(IViewRegistrar views)
    {
        views.Register<IWorkbenchViewModel, WorkbenchView>();
        views.Register<IFirstRunViewModel, FirstRunView>();
        views.Register<ISidebarViewModel, SidebarView>();
        views.Register<IJobRowViewModel, JobRowView>();
        views.Register<IToolbarViewModel, ToolbarView>();
        views.Register<IConversationViewModel, ConversationView>();
        views.Register<IComposerViewModel, ComposerView>();
        views.Register<IPromptViewModel, PromptView>();
        views.Register<IInterjectionViewModel, InterjectionView>();
        views.Register<IRestartViewModel, RestartView>();
        views.Register<IMessageViewModel, MessageView>();
        views.Register<IReasoningViewModel, ReasoningView>();
        views.Register<IToolViewModel, ToolView>();
        views.Register<IPlanViewModel, PlanView>();
        views.Register<IPlanPanelViewModel, PlanPanelView>();
        views.Register<ICanvasViewModel, CanvasView>();
        views.Register<ITurnEndViewModel, TurnEndView>();
        views.Register<IPermissionCardViewModel, PermissionCardView>();
        views.Register<IFormCardViewModel, FormCardView>();
        views.Register<IFormFieldViewModel, FormFieldView>();
        views.Register<IFormChoiceViewModel, FormChoiceView>();
        views.Register<IReviewViewModel, ReviewView>();
        views.Register<IReviewExceptionViewModel, ReviewExceptionView>();
        views.Register<IChangedFileViewModel, ChangedFileView>();
        views.Register<IHunkViewModel, HunkView>();
        views.Register<IDecisionsViewModel, DecisionsView>();
        views.Register<IDecisionViewModel, DecisionView>();
        views.Register<IEvidenceSectionViewModel, EvidenceSectionView>();
        views.Register<IAuditSectionViewModel, AuditSectionView>();
        views.Register<IUsageSectionViewModel, UsageSectionView>();
        views.Register<IAutonomySectionViewModel, AutonomySectionView>();
        views.Register<IWorktreeSectionViewModel, WorktreeSectionView>();
        views.Register<IDelegationSectionViewModel, DelegationSectionView>();
        RegisterGlobalViews(views);
    }

    private static void RegisterInspector(IPluginRegistrar registrar)
    {
        registrar.Services
            .AddSingleton<JobRecords>()
            .AddSingleton<JobAudit>()
            .AddSingleton<JobInspection>()
            .AddSingleton<InspectedFacts>()
            .AddTransient<InspectedJob>()
            .AddSingleton<EvidenceSectionViewModel>()
            .AddSingleton<AuditSectionViewModel>()
            .AddSingleton<UsageSectionViewModel>()
            .AddSingleton<AutonomySectionViewModel>()
            .AddSingleton<WorktreeSectionViewModel>()
            .AddSingleton<DelegationSectionViewModel>();
        registrar
            .AddToRegion<EvidenceSectionViewModel>(ShellRegions.Inspector, 10)
            .AddToRegion<AuditSectionViewModel>(ShellRegions.Inspector, 20)
            .AddToRegion<UsageSectionViewModel>(ShellRegions.Inspector, 30)
            .AddToRegion<AutonomySectionViewModel>(ShellRegions.Inspector, 40)
            .AddToRegion<WorktreeSectionViewModel>(ShellRegions.Inspector, 50)
            .AddToRegion<DelegationSectionViewModel>(ShellRegions.Inspector, 60);
    }

    private static void RegisterGlobalPages(IServiceCollection services) =>
        services
            .AddSingleton<Pulse>()
            .AddForwarded<IHandle<UsageRecorded>, Pulse>()
            .AddForwarded<IHandle<ResourcesSampled>, Pulse>()
            .AddForwarded<IHandle<OrphansFound>, Pulse>()
            .AddForwarded<IHandle<OrphansReaped>, Pulse>()
            .AddForwarded<IHandle<WorktreeReclaimed>, Pulse>()
            .AddForwarded<IHandle<WorktreesReconciled>, Pulse>()
            .AddForwarded<IHandle<ChildDelegated>, Pulse>()
            .AddForwarded<IHandle<DelegationRefused>, Pulse>()
            .AddForwarded<IHandle<ChildReported>, Pulse>()
            .AddForwarded<IHandle<BudgetCarved>, Pulse>()
            .AddForwarded<IHandle<BudgetIntervened>, Pulse>()
            .AddForwarded<IHandle<SupervisorIntervened>, Pulse>()
            .AddForwarded<IHandle<UpdateFound>, Pulse>()
            .AddForwarded<IHandle<TriggerFired>, Pulse>()
            .AddForwarded<IHandle<WebhookReceived>, Pulse>()
            .AddForwarded<IHandle<TriggersChanged>, Pulse>()
            .AddSingleton<SessionBook>()
            .AddForwarded<IHandle<SessionOpened>, SessionBook>()
            .AddForwarded<IHandle<JobSessionStarted>, SessionBook>()
            .AddSingleton<Leftovers>()
            .AddForwarded<IHandle<WorktreesReconciled>, Leftovers>()
            .AddTransient<LiveFeed>()
            .AddSingleton<JobSpending>()
            .AddSingleton<LimitReadings>()
            .AddSingleton<FleetReader>()
            .AddSingleton<DelegationReader>()
            .AddSingleton<UsageReader>()
            .AddSingleton<UsageWindows>()
            .AddSingleton<RulesReader>()
            .AddSingleton<RuleFileEditing>()
            .AddSingleton<RuleFileEditorViewModel>()
            .AddSingleton<MachineSettings>()
            .AddSingleton<SettingsFiles>()
            .AddSingleton<ResourceReader>()
            .AddSingleton<Housekeeping>()
            .AddSingleton<JobLaunch>()
            .AddSingleton<TriggerControls>()
            .AddSingleton<IConnectionsViewModel, ConnectionsViewModel>()
            .AddSingleton<IDelegationViewModel, DelegationViewModel>()
            .AddSingleton<IRepositorySettingsViewModel, RepositorySettingsViewModel>()
            .AddSingleton<IDefaultConnectionViewModel, DefaultConnectionViewModel>()
            .AddSingleton<ConnectionEditorViewModel>()
            .AddSingleton<IMachineSettingsViewModel, MachineSettingsViewModel>()
            .AddSingleton<IAppearanceViewModel, AppearanceViewModel>()
            .AddSingleton<IUpdateViewModel, UpdateViewModel>()
            .AddSingleton<IAboutViewModel, AboutViewModel>()
            .AddSingleton<UpdateNoticeViewModel>()
            .AddSingleton<ResourceIndicatorViewModel>()
            .AddTransient<ModelPickerViewModel>()
            .AddSingleton<NewJobReadings>()
            .AddSingleton<NewJobViewModel>()
            .AddSingleton<OverviewViewModel>()
            .AddSingleton<UsageViewModel>()
            .AddSingleton<ResourcesViewModel>()
            .AddSingleton<SettingsViewModel>()
            .AddSingleton<TriggersViewModel>()
            .AddForwarded<IPage, NewJobViewModel>()
            .AddForwarded<IPage, OverviewViewModel>()
            .AddForwarded<IPage, UsageViewModel>()
            .AddForwarded<IPage, ResourcesViewModel>()
            .AddForwarded<IPage, TriggersViewModel>()
            .AddForwarded<IPage, SettingsViewModel>();

    private static void RegisterGlobalViews(IViewRegistrar views)
    {
        views.Register<IOverviewViewModel, OverviewView>();
        views.Register<IConnectionsViewModel, ConnectionsView>();
        views.Register<IConnectionCardViewModel, ConnectionCardView>();
        views.Register<IAgentViewModel, AgentView>();
        views.Register<ILimitViewModel, LimitView>();
        views.Register<IDelegationViewModel, DelegationView>();
        views.Register<IOrchestratorViewModel, OrchestratorView>();
        views.Register<IOrchestratorCardViewModel, OrchestratorCardView>();
        views.Register<IDelegationNodeViewModel, DelegationNodeView>();
        views.Register<IDelegationRefusalViewModel, DelegationRefusalView>();
        views.Register<IUsageViewModel, UsageView>();
        views.Register<IConnectionMeterViewModel, ConnectionMeterView>();
        views.Register<IUsageWindowViewModel, UsageWindowView>();
        views.Register<IUsageRangeViewModel, UsageRangeView>();
        views.Register<IUsageDayViewModel, UsageDayView>();
        views.Register<IJobMeterViewModel, JobMeterView>();
        views.Register<IInterventionViewModel, InterventionView>();
        views.Register<ISettingsViewModel, SettingsView>();
        views.Register<IRepositorySettingsViewModel, RepositorySettingsView>();
        views.Register<IRuleFileEditorViewModel, RuleFileEditorView>();
        views.Register<IMachineSettingsViewModel, MachineSettingsView>();
        views.Register<IAppearanceViewModel, AppearanceView>();
        views.Register<IAboutViewModel, AboutView>();
        views.Register<IUpdateViewModel, UpdateView>();
        views.Register<IUpdateNoticeViewModel, UpdateNoticeView>();
        views.Register<IRuleFileViewModel, RuleFileView>();
        views.Register<IRuleViewModel, RuleView>();
        views.Register<ICapsViewModel, CapsView>();
        views.Register<ICheckViewModel, CheckView>();
        views.Register<IJobSectionViewModel, JobSectionView>();
        views.Register<IMachineConnectionViewModel, MachineConnectionView>();
        views.Register<IDefaultConnectionViewModel, DefaultConnectionView>();
        views.Register<IConnectionEditorViewModel, ConnectionEditorView>();
        views.Register<IResourcesViewModel, ResourcesView>();
        views.Register<IAgentTreeViewModel, AgentTreeView>();
        views.Register<IOrphanViewModel, OrphanView>();
        views.Register<IStaleWorktreeViewModel, StaleWorktreeView>();
        views.Register<IResourceIndicatorViewModel, ResourceIndicatorView>();
        views.Register<INewJobViewModel, NewJobView>();
        views.Register<IConnectionOptionViewModel, ConnectionOptionView>();
        views.Register<IModelPickerViewModel, ModelPickerView>();
        views.Register<ITriggersViewModel, TriggersView>();
        views.Register<ITriggerItemViewModel, TriggerItemView>();
    }
}
