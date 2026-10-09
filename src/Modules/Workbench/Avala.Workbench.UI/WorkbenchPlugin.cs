using Avala.Agents.Contracts;
using Avala.Budgets.Contracts;
using Avala.Delegation.Contracts;
using Avala.Observability.Contracts;
using Avala.Canvas.Contracts;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Sdk.UI;
using Avala.Verification.Contracts;
using Avala.Workbench.Board;
using Avala.Workbench.Cards;
using Avala.Workbench.Conversation;
using Avala.Workbench.Decisions;
using Avala.Workbench.Inspection;
using Avala.Workbench.Inspector;
using Avala.Workbench.Navigation;
using Avala.Workbench.Replies;
using Avala.Workbench.Review;
using Avala.Workbench.Reviewing;
using Avala.Workbench.Sidebar;
using Avala.Workbench.Steering;
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
            .AddSingleton<IHandle<PermissionAnswered>>(Keeper)
            .AddSingleton<IHandle<AutonomyApplied>>(Keeper)
            .AddSingleton<IHandle<UsageRecorded>>(Keeper)
            .AddSingleton<IHandle<BudgetIntervened>>(Keeper)
            .AddSingleton<IHandle<BudgetCarved>>(Keeper)
            .AddSingleton<IHandle<ChildDelegated>>(Keeper)
            .AddSingleton<IHandle<ChildReported>>(Keeper)
            .AddSingleton<JobSteering>()
            .AddSingleton<HumanReplies>()
            .AddSingleton<Conversations>()
            .AddSingleton<ReviewReader>()
            .AddSingleton<ReviewDesk>()
            .AddSingleton<Reviews>()
            .AddSingleton<JobRecords>()
            .AddSingleton<JobAudit>()
            .AddSingleton<JobInspection>()
            .AddSingleton<Inspectors>()
            .AddSingleton<JobScreens>()
            .AddSingleton<DecisionsViewModel>()
            .AddSingleton<SidebarViewModel>()
            .AddSingleton<WorkbenchViewModel>()
            .AddSingleton<IPage>(services => services.GetRequiredService<WorkbenchViewModel>());
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
        views.Register<ReviewViewModel, ReviewView>();
        views.Register<ReviewExceptionViewModel, ReviewExceptionView>();
        views.Register<ChangedFileViewModel, ChangedFileView>();
        views.Register<HunkViewModel, HunkView>();
        views.Register<DecisionsViewModel, DecisionsView>();
        views.Register<DecisionViewModel, DecisionView>();
        views.Register<InspectorViewModel, InspectorView>();
        views.Register<EvidenceSectionViewModel, EvidenceSectionView>();
        views.Register<AuditSectionViewModel, AuditSectionView>();
        views.Register<UsageSectionViewModel, UsageSectionView>();
        views.Register<AutonomySectionViewModel, AutonomySectionView>();
        views.Register<WorktreeSectionViewModel, WorktreeSectionView>();
        views.Register<DelegationSectionViewModel, DelegationSectionView>();
    }

    private static BoardKeeper Keeper(IServiceProvider services) => services.GetRequiredService<BoardKeeper>();
}
