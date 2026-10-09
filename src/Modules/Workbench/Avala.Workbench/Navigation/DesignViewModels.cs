using Avala.Workbench.Conversation;
using Avala.Workbench.Review;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Navigation;

internal sealed class DesignWorkbenchViewModel : IWorkbenchViewModel
{
    public string Title => "Jobs";

    public IConversationViewModel? Conversation { get; } = new DesignConversationViewModel();

    public IReviewViewModel? Review => null;

    public bool IsInspectorOpen => true;

    public IRelayCommand ToggleInspectorCommand { get; } = new RelayCommand(() => { });

    public IAsyncRelayCommand OpenReviewCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask, () => false);

    public IRelayCommand CloseReviewCommand { get; } = new RelayCommand(() => { });
}
