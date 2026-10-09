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

    public IFirstRunViewModel FirstRun { get; init; } = new DesignFirstRunViewModel { IsShown = false };
}

internal sealed class DesignFirstRunViewModel : IFirstRunViewModel
{
    public bool IsShown { get; init; } = true;

    public string Heading => "No connections yet";

    public string Explanation =>
        "A job runs on a harness, and Avala found none to run it on: no Claude Code login on this computer and no connection declared in connections.json.";

    public IRelayCommand OpenSettingsCommand { get; } = new RelayCommand(() => { });

    public Task CheckAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
