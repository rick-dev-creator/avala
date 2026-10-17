using Avala.Agents.Contracts;
using Avala.Autopilot.Contracts;
using Avala.Components.Pictures;
using Avala.Components.Privacy;
using Avala.Components.UI.Copying;
using Avala.Components.UI.Folders;
using Avala.Components.UI.Pictures;
using Avala.Workbench.Timeline;
using Avala.Budgets.Contracts;
using Avala.Canvas.Contracts;
using Avala.Contests.Contracts;
using Avala.Delegation.Contracts;
using Avala.Forges.Contracts;
using Avala.Handoffs.Contracts;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Permissions.Contracts;
using Avala.Resources.Contracts;
using Avala.Sdk;
using Avala.Sdk.Appearance;
using Avala.Sdk.Events;
using Avala.Sdk.Regions;
using Avala.Sdk.UI;
using Avala.Sdk.Updates;
using Avala.Supervision.Contracts;
using Avala.Triggers.Contracts;
using Avala.Verification.Contracts;
using Avala.Workbench.Accounts;
using Avala.Workbench.Archive;
using Avala.Workbench.Archiving;
using Avala.Workbench.Automation;
using Avala.Workbench.Board;
using Avala.Workbench.Drafting;
using Avala.Workbench.Drafting.Storage;
using Avala.Workbench.Cards;
using Avala.Workbench.ModelChoices;
using Avala.Workbench.Conversation;
using Avala.Workbench.Mentioning;
using Avala.Workbench.Decisions;
using Avala.Workbench.Fleet;
using Avala.Workbench.Following;
using Avala.Workbench.Harnesses;
using Avala.Workbench.Inspection;
using Avala.Workbench.Inspector;
using Avala.Workbench.Linking;
using Avala.Workbench.Machine;
using Avala.Workbench.Navigation;
using Avala.Workbench.NewJob;
using Avala.Workbench.Onboarding;
using Avala.Workbench.Overview;
using Avala.Workbench.Presenting;
using Avala.Workbench.Remoting;
using Avala.Workbench.Replies;
using Avala.Workbench.RepositoryChoice;
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
using Avala.Workbench.Telemetry;
using Avala.Projects.Contracts;
using Avala.Workbench.Projects;
using Avala.Workbench.Updates;
using Avala.Workbench.Upkeep;
using Avala.Workbench.Usage;
using Avala.Workbench.Welcome;
using Avala.Workspaces.Contracts;
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
            .AddSingleton<TranscriptRecall>()
            .AddSingleton<BoardJoiner>()
            .AddSingleton<BoardKeeper>()
            .AddForwarded<IHandle<StartupCompleted>, BoardKeeper>()
            .AddForwarded<IHandle<JobSubmitted>, BoardKeeper>()
            .AddForwarded<IHandle<JobProgressed>, BoardKeeper>()
            .AddForwarded<IHandle<JobHeld>, BoardKeeper>()
            .AddForwarded<IHandle<JobApproved>, BoardKeeper>()
            .AddForwarded<IHandle<JobModelChosen>, BoardKeeper>()
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
            .AddForwarded<IHandle<BudgetRaised>, BoardKeeper>()
            .AddForwarded<IHandle<ChildDelegated>, BoardKeeper>()
            .AddForwarded<IHandle<ChildReported>, BoardKeeper>()
            .AddForwarded<IHandle<DelegationRefused>, BoardKeeper>()
            .AddForwarded<IHandle<HandoffRecorded>, BoardKeeper>()
            .AddForwarded<IHandle<JobWaitsForReset>, BoardKeeper>()
            .AddForwarded<IHandle<ParentAsked>, BoardKeeper>()
            .AddForwarded<IHandle<PullRequestWatchChanged>, BoardKeeper>()
            .AddForwarded<IHandle<PullRequestWakeUp>, BoardKeeper>()
            .AddTransient<BoardFeed>()
            .AddSingleton<JobFocus>()
            .AddSingleton<JobSteering>()
            .AddSingleton<QueuedMessages>()
            .AddForwarded<IHandle<JobProgressed>, QueuedMessages>()
            .AddSingleton<DeniedRetries>()
            .AddSingleton<HumanReplies>()
            .AddSingleton<SendKeys>()
            .AddForwarded<IHandle<AppearanceChanged>, SendKeys>()
            .AddSingleton<PersonalDetailsViewModel>()
            .AddForwarded<IPersonalDetailsViewModel, PersonalDetailsViewModel>()
            .AddForwarded<IHandle<AppearanceChanged>, PersonalDetailsViewModel>()
            .AddSingleton<TimelineItems>()
            .AddSingleton<WorkedFiles>()
            .AddSingleton<FileViewing>()
            .AddSingleton<Conversations>()
            .AddSingleton<Composers>()
            .AddSingleton<JobHarnesses>()
            .AddSingleton<JobSwitching>()
            .AddSingleton<TranscriptExport>()
            .AddSingleton<ReviewReader>()
            .AddSingleton<ConflictFinder>()
            .AddSingleton<ForgeDesk>()
            .AddSingleton<ReviewDesk>()
            .AddSingleton<Reviews>()
            .AddSingleton<JobRegions>()
            .AddSingleton<JobScreens>()
            .AddSingleton<DecisionsViewModel>()
            .AddForwarded<IDecisionsViewModel, DecisionsViewModel>()
            .AddSingleton<SidebarViewModel>()
            .AddSingleton<ToolbarViewModel>()
            .AddSingleton<SettingsLink>()
            .AddSingleton<IWelcomeLinks, WelcomeLinks>()
            .AddSingleton<WelcomeHarness>()
            .AddSingleton<IWelcomeViewModel, WelcomeViewModel>()
            .AddSingleton<WorkbenchViewModel>()
            .AddForwarded<IPage, WorkbenchViewModel>();
        RegisterReplies(registrar.Services);
        RegisterRemoteControl(registrar.Services);
        RegisterRegions(registrar);
        RegisterInspector(registrar);
        RegisterGlobalPages(registrar.Services);
        RegisterAccounts(registrar.Services);
        RegisterRepositoryChoice(registrar.Services);
        RegisterBoardGroups(registrar);
        RegisterFooter(registrar);
    }

    private static void RegisterReplies(IServiceCollection services) =>
        services
            .AddSingleton<Links>()
            .AddSingleton<CodeFiles>()
            .AddSingleton<JobReferences>()
            .AddSingleton<ReplyTools>();

    private static void RegisterRemoteControl(IServiceCollection services) =>
        services
            .AddSingleton<RemoteKeeper>()
            .AddForwarded<IHandle<SessionOpened>, RemoteKeeper>()
            .AddForwarded<IHandle<JobSessionStarted>, RemoteKeeper>()
            .AddForwarded<IHandle<RemoteControlReported>, RemoteKeeper>()
            .AddForwarded<IHandle<JobRemoteControlChosen>, RemoteKeeper>()
            .AddSingleton<LinkTools>()
            .AddSingleton<RemoteControlViewModel>()
            .AddSingleton<AttachmentTrays>()
            .AddSingleton<MentionSource>()
            .AddSingleton<CheckpointKeeper>()
            .AddForwarded<IHandle<JobProgressed>, CheckpointKeeper>()
            .AddForwarded<IHandle<StartupCompleted>, CheckpointKeeper>()
            .AddSingleton<IDraftStore, DraftFile>()
            .AddSingleton<DraftBook>()
            .AddForwarded<IStartupTask, DraftBook>()
            .AddSingleton<ComposerParts>();

    private static void RegisterRegions(IPluginRegistrar registrar) =>
        registrar.AddToRegion<SidebarViewModel>(ShellRegions.Sidebar, 0)
            .AddToRegion<ToolbarViewModel>(ShellRegions.Toolbar, 0)
            .AddToRegion<DelegationToggleViewModel>(WorkbenchRegions.JobHeader, 1)
            .AddToRegion<RemoteControlViewModel>(WorkbenchRegions.JobHeader, 2)
            .AddToRegion<ArchivedNoteViewModel>(WorkbenchRegions.JobHeader, 3)
            .AddToRegion<ContextLeftViewModel>(WorkbenchRegions.JobHeader, 4)
            .AddToRegion<UsageLeftViewModel>(WorkbenchRegions.JobHeader, 5)
            .AddToRegion<ChecksRunningViewModel>(WorkbenchRegions.JobHeader, 6)
            .AddToRegion<DelegationPaneViewModel>(WorkbenchRegions.Preview, 10);

    private static void RegisterProjects(IServiceCollection services) =>
        services
            .AddSingleton<ProjectKeeper>()
            .AddForwarded<IHandle<ProjectsRestored>, ProjectKeeper>()
            .AddForwarded<IHandle<ProjectCreated>, ProjectKeeper>()
            .AddForwarded<IHandle<ProjectRenamed>, ProjectKeeper>()
            .AddForwarded<IHandle<ProjectDeleted>, ProjectKeeper>()
            .AddForwarded<IHandle<ConversationMoved>, ProjectKeeper>()
            .AddForwarded<IHandle<ConversationsArchived>, ProjectKeeper>()
            .AddForwarded<IHandle<ConversationRestored>, ProjectKeeper>()
            .AddSingleton<ProjectShelf>()
            .AddSingleton<ArchivedReader>()
            .AddSingleton<ArchivedViewModel>()
            .AddForwarded<IPage, ArchivedViewModel>()
            .AddSingleton<ArchivedEntryViewModel>()
            .AddSingleton<ArchivedNoteViewModel>();

    private static void RegisterFooter(IPluginRegistrar registrar)
    {
        registrar.AddToRegion<ArchivedEntryViewModel>(ShellRegions.SidebarFooter, -10);
        registrar.AddToRegion<ResourceIndicatorViewModel>(ShellRegions.SidebarFooter, 0);
        registrar.AddToRegion<UpdateNoticeViewModel>(ShellRegions.SidebarFooter, 10);
        RegisterTelemetry(registrar);
    }

    private static void RegisterTelemetry(IPluginRegistrar registrar)
    {
        registrar.Services
            .AddSingleton<ITelemetryViewModel, TelemetryViewModel>()
            .AddSingleton<TelemetryNoticeViewModel>();
        registrar.AddToRegion<TelemetryNoticeViewModel>(ShellRegions.SidebarFooter, 20);
        RegisterOnboarding(registrar);
    }

    private static void RegisterOnboarding(IPluginRegistrar registrar)
    {
        registrar.Services
            .AddSingleton<IAgentsStepViewModel, AgentsStepViewModel>()
            .AddSingleton<ICodeStepViewModel, CodeStepViewModel>()
            .AddSingleton<IPrivacyStepViewModel, PrivacyStepViewModel>()
            .AddSingleton(provider => new OnboardingSteps(
                new WelcomeStepViewModel(),
                provider.GetRequiredService<IAgentsStepViewModel>(),
                provider.GetRequiredService<ICodeStepViewModel>(),
                provider.GetRequiredService<IPrivacyStepViewModel>()))
            .AddSingleton<OnboardingViewModel>();
        registrar.AddToRegion<OnboardingViewModel>(ShellRegions.Overlay, 0);
    }

    public void RegisterViews(IViewRegistrar views)
    {
        views.Register<IWorkbenchViewModel, WorkbenchView>();
        views.Register<IWelcomeViewModel, WelcomeView>();
        views.Register<IAccountChipViewModel, AccountChipView>();
        views.Register<IHarnessChoiceViewModel, HarnessChoiceView>();
        views.Register<IHarnessOptionViewModel, HarnessOptionView>();
        views.Register<ISidebarViewModel, SidebarView>();
        views.Register<IProjectGroupViewModel, ProjectGroupView>();
        views.Register<IJobRowViewModel, JobRowView>();
        views.Register<IContestRowViewModel, ContestRowView>();
        views.Register<ICandidateRowViewModel, CandidateRowView>();
        views.Register<IToolbarViewModel, ToolbarView>();
        views.Register<IConversationViewModel, ConversationView>();
        views.Register<IComposerViewModel, ComposerView>();
        views.Register<IPromptViewModel, PromptView>();
        views.Register<IInterjectionViewModel, InterjectionView>();
        views.Register<IChildAskingViewModel, ChildAskingView>();
        views.Register<IRestartViewModel, RestartView>();
        views.Register<ICiViewModel, CiView>();
        views.Register<IMessageViewModel, MessageView>();
        views.Register<IWorkingViewModel, WorkingView>();
        views.Register<IReasoningViewModel, ReasoningView>();
        views.Register<IToolViewModel, ToolView>();
        views.Register<IFileChangesViewModel, FileChangesView>();
        views.Register<IFilePreviewViewModel, FilePreviewView>();
        views.Register<IPlanViewModel, PlanView>();
        views.Register<IPlanPanelViewModel, PlanPanelView>();
        views.Register<IPlanStepViewModel, PlanStepView>();
        views.Register<ICanvasViewModel, CanvasView>();
        views.Register<ITurnEndViewModel, TurnEndView>();
        views.Register<IPermissionCardViewModel, PermissionCardView>();
        views.Register<IDeniedCardViewModel, DeniedCardView>();
        views.Register<IAnswerScopeViewModel, AnswerScopeView>();
        views.Register<IFormCardViewModel, FormCardView>();
        views.Register<IFormFieldViewModel, FormFieldView>();
        views.Register<IFormChoiceViewModel, FormChoiceView>();
        views.Register<IReviewViewModel, ReviewView>();
        views.Register<IReviewExceptionViewModel, ReviewExceptionView>();
        views.Register<IChangedFileViewModel, ChangedFileView>();
        views.Register<IHunkViewModel, HunkView>();
        views.Register<IEditDiffViewModel, EditDiffView>();
        views.Register<ICodePeekViewModel, CodePeekView>();
        views.Register<IDecisionsViewModel, DecisionsView>();
        views.Register<IDecisionViewModel, DecisionView>();
        views.Register<IEvidenceSectionViewModel, EvidenceSectionView>();
        views.Register<IAuditSectionViewModel, AuditSectionView>();
        views.Register<IUsageSectionViewModel, UsageSectionView>();
        views.Register<IAutonomySectionViewModel, AutonomySectionView>();
        views.Register<IWorktreeSectionViewModel, WorktreeSectionView>();
        views.Register<IDelegationSectionViewModel, DelegationSectionView>();
        views.Register<IPullRequestSectionViewModel, PullRequestSectionView>();
        views.Register<IContestSectionViewModel, ContestSectionView>();
        views.Register<ICandidateSwitcherViewModel, CandidateSwitcherView>();
        RegisterGlobalViews(views);
        RegisterDocumentationViews(views);
        RegisterFollowingViews(views);
    }

    private static void RegisterInspector(IPluginRegistrar registrar)
    {
        registrar.Services
            .AddSingleton<JobRecords>()
            .AddSingleton<JobAudit>()
            .AddSingleton<RaiseDesk>()
            .AddSingleton<JobInspection>()
            .AddSingleton<InspectedFacts>()
            .AddTransient<InspectedJob>()
            .AddSingleton<EvidenceSectionViewModel>()
            .AddSingleton<AuditSectionViewModel>()
            .AddSingleton<UsageSectionViewModel>()
            .AddSingleton<AutonomySectionViewModel>()
            .AddSingleton<WorktreeSectionViewModel>()
            .AddSingleton<DelegationSectionViewModel>()
            .AddTransient<PublishRepositoryViewModel>()
            .AddSingleton<PullRequestSectionViewModel>();
        registrar
            .AddToRegion<EvidenceSectionViewModel>(ShellRegions.Inspector, 10)
            .AddToRegion<AuditSectionViewModel>(ShellRegions.Inspector, 20)
            .AddToRegion<UsageSectionViewModel>(ShellRegions.Inspector, 30)
            .AddToRegion<AutonomySectionViewModel>(ShellRegions.Inspector, 40)
            .AddToRegion<WorktreeSectionViewModel>(ShellRegions.Inspector, 50)
            .AddToRegion<DelegationSectionViewModel>(ShellRegions.Inspector, 60)
            .AddToRegion<PullRequestSectionViewModel>(ShellRegions.Inspector, 55);
    }

    private static void RegisterBoardGroups(IPluginRegistrar registrar)
    {
        RegisterContests(registrar);
        RegisterProjects(registrar.Services);
    }

    private static void RegisterContests(IPluginRegistrar registrar)
    {
        registrar.Services
            .AddSingleton<ContestKeeper>()
            .AddForwarded<IHandle<ContestsRestored>, ContestKeeper>()
            .AddForwarded<IHandle<ContestStarted>, ContestKeeper>()
            .AddForwarded<IHandle<CandidateSettled>, ContestKeeper>()
            .AddForwarded<IHandle<CandidateReopened>, ContestKeeper>()
            .AddForwarded<IHandle<ContestDecided>, ContestKeeper>()
            .AddForwarded<IHandle<ContestUndecided>, ContestKeeper>()
            .AddForwarded<IHandle<ContestClosed>, ContestKeeper>()
            .AddSingleton<Submissions>()
            .AddSingleton<ScratchFolders>()
            .AddSingleton<RecentRepositories>()
            .AddSingleton<ContestReadings>()
            .AddTransient<JobOptionsViewModel>()
            .AddSingleton<ContestSectionViewModel>();
        registrar.AddToRegion<ContestSectionViewModel>(ShellRegions.Inspector, 15);
    }

    private static void RegisterRepositoryChoice(IServiceCollection services)
    {
        services.TryAddSingleton<IFolderPicker, AvaloniaFolderPicker>();
        services.TryAddSingleton<IFilePicker, AvaloniaFilePicker>();
        services.TryAddSingleton<IClipboard, AvaloniaClipboard>();
        services.TryAddSingleton<IPictureDecoder>(_ => new PictureCache());
        services.TryAddSingleton<IFileSaver, AvaloniaFileSaver>();
        services
            .AddTransient<PublishOptionsViewModel>()
            .AddTransient<NewRepositoryViewModel>()
            .AddSingleton<ProjectsPlace>()
            .AddSingleton(provider => new CloneServices(
                provider.GetRequiredService<SourceControl>(),
                provider.GetRequiredService<IRemoteRepositories>(),
                provider.GetRequiredService<ProjectsPlace>(),
                provider.GetRequiredService<TimeProvider>()))
            .AddTransient<CloneSheetViewModel>()
            .AddTransient<RepositoryStarts>()
            .AddTransient<RepositoryFieldViewModel>()
            .AddTransient<RepositoryPickerViewModel>()
            .AddTransient<FeatureChipViewModel>()
            .AddTransient<NewJobFields>();
    }

    private static void RegisterAccounts(IServiceCollection services) =>
        services
            .AddSingleton<SourceControl>()
            .AddSingleton<ConnectSheetViewModel>()
            .AddSingleton<SourceControlViewModel>()
            .AddForwarded<ISourceControlViewModel, SourceControlViewModel>()
            .AddSingleton(provider => new MachineSections(
                provider.GetRequiredService<IDefaultConnectionViewModel>(),
                provider.GetRequiredService<INotificationsViewModel>(),
                provider.GetRequiredService<ISourceControlViewModel>(),
                provider.GetRequiredService<IPersonalDetailsViewModel>())
            {
                Harnesses = [.. provider.GetServices<RegionContribution>().Where(contribution => contribution.Region == WorkbenchRegions.Settings).OrderBy(contribution => contribution.Order).Select(contribution => contribution.ViewModel)],
            });

    private static IServiceCollection RegisterPulse(IServiceCollection services) =>
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
            .AddForwarded<IHandle<BudgetRaised>, Pulse>()
            .AddForwarded<IHandle<BudgetIntervened>, Pulse>()
            .AddForwarded<IHandle<SupervisorIntervened>, Pulse>()
            .AddForwarded<IHandle<UpdateFound>, Pulse>()
            .AddForwarded<IHandle<UpdateDownloaded>, Pulse>()
            .AddForwarded<IHandle<TriggerFired>, Pulse>()
            .AddForwarded<IHandle<WebhookReceived>, Pulse>()
            .AddForwarded<IHandle<TriggersChanged>, Pulse>()
            .AddForwarded<IHandle<ForgeEventsChanged>, Pulse>()
            .AddForwarded<IHandle<LoopStarted>, Pulse>()
            .AddForwarded<IHandle<LoopTaskTaken>, Pulse>()
            .AddForwarded<IHandle<LoopIterated>, Pulse>()
            .AddForwarded<IHandle<LoopIterationFollowed>, Pulse>()
            .AddForwarded<IHandle<LoopWaiting>, Pulse>()
            .AddForwarded<IHandle<LoopPaused>, Pulse>()
            .AddForwarded<IHandle<LoopResumed>, Pulse>()
            .AddForwarded<IHandle<BreakerTripped>, Pulse>()
            .AddForwarded<IHandle<LoopEnded>, Pulse>();

    private static void RegisterGlobalPages(IServiceCollection services) =>
        RegisterPulse(services)
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
            .AddSingleton<RowActions>()
            .AddSingleton<UsageReader>()
            .AddSingleton<WindowWatch>()
            .AddSingleton<JobAllowance>()
            .AddSingleton<UsageWindows>()
            .AddSingleton<RulesReader>()
            .AddSingleton<RuleFileEditing>()
            .AddSingleton<RuleFileEditorViewModel>()
            .AddSingleton<MachineSettings>()
            .AddSingleton<SettingsFiles>()
            .AddSingleton<PhoneNotifications>()
            .AddSingleton<INotificationsViewModel, NotificationsViewModel>()
            .AddSingleton<ResourceReader>()
            .AddSingleton<Housekeeping>()
            .AddSingleton<JobLaunch>()
            .AddSingleton<TriggerControls>()
            .AddSingleton<LoopControls>()
            .AddSingleton<ILoopsViewModel, LoopsViewModel>()
            .AddSingleton<IConnectionsViewModel, ConnectionsViewModel>()
            .AddSingleton<IDelegationViewModel, DelegationViewModel>()
            .AddSingleton<DelegationPaneViewModel>()
            .AddSingleton<DelegationToggleViewModel>()
            .AddSingleton<DocsSettingViewModel>()
            .AddSingleton<RepositorySettingsParts>()
            .AddSingleton<IRepositorySettingsViewModel, RepositorySettingsViewModel>()
            .AddSingleton<IDefaultConnectionViewModel, DefaultConnectionViewModel>()
            .AddSingleton<ConnectionEditorViewModel>()
            .AddSingleton<IMachineSettingsViewModel, MachineSettingsViewModel>()
            .AddSingleton<IAppearanceViewModel, AppearanceViewModel>()
            .AddSingleton<IUpdateViewModel, UpdateViewModel>()
            .AddSingleton<AboutLinks>()
            .AddSingleton<IAboutViewModel, AboutViewModel>()
            .AddSingleton<UpdateNoticeViewModel>()
            .AddSingleton<ResourceIndicatorViewModel>()
            .AddSingleton<UsageLeftViewModel>()
            .AddSingleton<ContextLeftViewModel>()
            .AddSingleton<ChecksRunningViewModel>()
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

    private static void RegisterFollowingViews(IViewRegistrar views)
    {
        views.Register<IRemoteControlViewModel, RemoteControlView>();
        views.Register<IConversationSearchViewModel, ConversationSearchView>();
        views.Register<IAttachmentTrayViewModel, AttachmentTrayView>();
        views.Register<IAttachmentChipViewModel, AttachmentChipView>();
        views.Register<IMentionsViewModel, MentionsView>();
        views.Register<IMentionRowViewModel, MentionRowView>();
        views.Register<ISubagentViewModel, SubagentView>();
        views.Register<IDelegationPaneViewModel, DelegationPaneView>();
        views.Register<IDelegationToggleViewModel, DelegationToggleView>();
    }

    private static void RegisterDocumentationViews(IViewRegistrar views)
    {
        views.Register<IFeatureChipViewModel, FeatureChipView>();
        views.Register<IFeatureOptionViewModel, FeatureOptionView>();
        views.Register<IDocsSettingViewModel, DocsSettingView>();
    }

    private static void RegisterGlobalViews(IViewRegistrar views)
    {
        views.Register<IOverviewViewModel, OverviewView>();
        views.Register<IConnectionsViewModel, ConnectionsView>();
        views.Register<IConnectionCardViewModel, ConnectionCardView>();
        views.Register<IAgentViewModel, AgentView>();
        views.Register<ILimitViewModel, LimitView>();
        views.Register<IDelegationViewModel, DelegationView>();
        views.Register<IOrchestratorCardViewModel, OrchestratorCardView>();
        views.Register<IDelegationNodeViewModel, DelegationNodeView>();
        views.Register<IRecentActionViewModel, RecentActionView>();
        views.Register<IPeekViewModel, PeekView>();
        views.Register<IOrchestrationViewModel, OrchestrationView>();
        views.Register<IDelegationRefusalViewModel, DelegationRefusalView>();
        views.Register<IUsageViewModel, UsageView>();
        views.Register<IConnectionMeterViewModel, ConnectionMeterView>();
        views.Register<IUsageLeftViewModel, UsageLeftView>();
        views.Register<IContextLeftViewModel, ContextLeftView>();
        views.Register<IChecksRunningViewModel, ChecksRunningView>();
        views.Register<IArchivedViewModel, ArchivedView>();
        views.Register<IArchivedRowViewModel, ArchivedRowView>();
        views.Register<IArchivedEntryViewModel, ArchivedEntryView>();
        views.Register<IArchivedNoteViewModel, ArchivedNoteView>();
        views.Register<IUsageWindowViewModel, UsageWindowView>();
        views.Register<IUsageRangeViewModel, UsageRangeView>();
        views.Register<IUsageDayViewModel, UsageDayView>();
        views.Register<IJobMeterViewModel, JobMeterView>();
        views.Register<IInterventionViewModel, InterventionView>();
        views.Register<ISettingsViewModel, SettingsView>();
        views.Register<IRepositorySettingsViewModel, RepositorySettingsView>();
        views.Register<IRuleFileEditorViewModel, RuleFileEditorView>();
        views.Register<IMachineSettingsViewModel, MachineSettingsView>();
        views.Register<IMachineAccountsViewModel, MachineAccountsView>();
        views.Register<INotificationsViewModel, NotificationsView>();
        views.Register<IAppearanceViewModel, AppearanceView>();
        views.Register<IAboutViewModel, AboutView>();
        views.Register<IUpdateViewModel, UpdateView>();
        views.Register<IUpdateNoticeViewModel, UpdateNoticeView>();
        views.Register<ITelemetryViewModel, TelemetryView>();
        views.Register<ITelemetryNoticeViewModel, TelemetryNoticeView>();
        views.Register<IOnboardingViewModel, OnboardingView>();
        views.Register<IWelcomeStepViewModel, WelcomeStepView>();
        views.Register<IAgentsStepViewModel, AgentsStepView>();
        views.Register<IAgentRowViewModel, AgentRowView>();
        views.Register<IInstallRowViewModel, InstallRowView>();
        views.Register<ICodeStepViewModel, CodeStepView>();
        views.Register<IPrivacyStepViewModel, PrivacyStepView>();
        views.Register<IRuleFileViewModel, RuleFileView>();
        views.Register<IRuleViewModel, RuleView>();
        views.Register<ICapsViewModel, CapsView>();
        views.Register<ICheckViewModel, CheckView>();
        views.Register<IJobSectionViewModel, JobSectionView>();
        views.Register<IMachineConnectionViewModel, MachineConnectionView>();
        views.Register<ISourceControlViewModel, SourceControlView>();
        views.Register<IAccountRowViewModel, AccountRowView>();
        views.Register<IConnectSheetViewModel, ConnectSheetView>();
        views.Register<IForgeKindViewModel, ForgeKindView>();
        views.Register<IDefaultConnectionViewModel, DefaultConnectionView>();
        views.Register<IConnectionEditorViewModel, ConnectionEditorView>();
        views.Register<IResourcesViewModel, ResourcesView>();
        views.Register<IAgentTreeViewModel, AgentTreeView>();
        views.Register<IOrphanViewModel, OrphanView>();
        views.Register<IStaleWorktreeViewModel, StaleWorktreeView>();
        views.Register<IResourceIndicatorViewModel, ResourceIndicatorView>();
        views.Register<INewJobViewModel, NewJobView>();
        views.Register<IRepositoryFieldViewModel, RepositoryFieldView>();
        views.Register<IRepositoryPickerViewModel, RepositoryPickerView>();
        views.Register<IRepositoryChoiceViewModel, RepositoryChoiceView>();
        views.Register<INewRepositoryViewModel, NewRepositoryView>();
        views.Register<ICloneSheetViewModel, CloneSheetView>();
        views.Register<ICloneSourceViewModel, CloneSourceView>();
        views.Register<IRemoteRowViewModel, RemoteRowView>();
        views.Register<IPublishOptionsViewModel, PublishOptionsView>();
        views.Register<IPublishRepositoryViewModel, PublishRepositoryView>();
        views.Register<IConnectionOptionViewModel, ConnectionOptionView>();
        views.Register<IModelPickerViewModel, ModelPickerView>();
        views.Register<IJobOptionsViewModel, JobOptionsView>();
        views.Register<ITriggersViewModel, TriggersView>();
        views.Register<ITriggerItemViewModel, TriggerItemView>();
        views.Register<ILoopsViewModel, LoopsView>();
        views.Register<ILoopItemViewModel, LoopItemView>();
    }
}
