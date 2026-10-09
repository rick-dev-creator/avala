using Avala.Sdk;
using Avala.Workbench.Conversation;
using Avala.Workbench.Review;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Navigation;

internal sealed class DesignWorkbenchViewModel(Option<IConversationViewModel> conversation, bool isInspectorOpen) : IWorkbenchViewModel, IPage
{
    public DesignWorkbenchViewModel()
        : this(Option<IConversationViewModel>.Some(new DesignConversationViewModel()), false)
    {
    }

    public string Title => "Jobs";

    public PagePlacement Placement => PagePlacement.Hidden;

    public IConversationViewModel? Conversation { get; } = conversation.Match<IConversationViewModel?>(open => open, () => null);

    public IReviewViewModel? Review => null;

    public bool IsInspectorOpen { get; } = isInspectorOpen;

    public IRelayCommand ToggleInspectorCommand { get; } = new RelayCommand(() => { });

    public IAsyncRelayCommand OpenReviewCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask, () => false);

    public IRelayCommand CloseReviewCommand { get; } = new RelayCommand(() => { });
}
