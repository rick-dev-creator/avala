using Avala.Agents.Contracts;
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
using Avala.Workbench.Navigation;
using Avala.Workbench.Replies;
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
            .AddSingleton<JobSteering>()
            .AddSingleton<HumanReplies>()
            .AddSingleton<Conversations>()
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
    }

    private static BoardKeeper Keeper(IServiceProvider services) => services.GetRequiredService<BoardKeeper>();
}
