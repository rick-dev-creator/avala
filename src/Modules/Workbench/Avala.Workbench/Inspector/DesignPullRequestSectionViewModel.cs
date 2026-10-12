using Avala.Workbench.RepositoryChoice;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Inspector;

internal sealed class DesignPullRequestSectionViewModel : IPullRequestSectionViewModel
{
    public bool IsLoaded => true;

    public bool HasPullRequest => true;

    public bool IsWatching => true;

    public string Fact => "#7 · watching";

    public string Title => "Pull request #7 on github";

    public string Link => "https://github.com/octo/shop/pull/7";

    public string Status => "Watching · next check 14:05";

    public string Policy => "Wakes the agent on CI failures and conflicts, at most 3 times";

    public string Checks => "1 passed · 1 failed · 0 pending";

    public IReadOnlyList<string> CheckLines { get; } = ["passed · build", "failed · tests: 1 test failed"];

    public string Mergeability => "No conflicts";

    public IReadOnlyList<string> Reviews { get; } = ["lisa approved"];

    public IReadOnlyList<string> WakeUps { get; } = ["Wake-up 1 at 14:02: checks failed, the agent resumed its conversation"];

    public string Notice => string.Empty;

    public IPublishRepositoryViewModel Publishing { get; init; } = new DesignPublishRepositoryViewModel { IsShown = false };

    public IAsyncRelayCommand OpenLinkCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand CheckNowCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);
}

internal sealed class DesignPublishRepositoryViewModel : IPublishRepositoryViewModel
{
    public bool IsShown { get; init; } = true;

    public string Remote { get; init; } = PublishPhrases.NoRemote;

    public bool CanOffer { get; init; } = true;

    public bool IsOpen { get; init; } = true;

    public IPublishOptionsViewModel Options { get; init; } = new DesignPublishOptionsViewModel { Name = "shop" };

    public string Error { get; init; } = string.Empty;

    public string Notice { get; init; } = string.Empty;

    public IAsyncRelayCommand StartCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand PublishCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IRelayCommand CancelCommand { get; } = new RelayCommand(() => { });
}
