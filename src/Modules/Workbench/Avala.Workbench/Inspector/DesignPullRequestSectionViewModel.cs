using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Inspector;

internal sealed class DesignPullRequestSectionViewModel : IPullRequestSectionViewModel
{
    public bool IsLoaded => true;

    public bool HasPullRequest => true;

    public string Fact => "#7 · Watching";

    public string Title => "Pull request #7 on github";

    public string Link => "https://github.com/octo/shop/pull/7";

    public string Status => "Watching · next check 14:05";

    public string Policy => "Wakes the agent on CI failures and conflicts, at most 3 times";

    public string Checks => "1 passed · 1 failed · 0 pending";

    public IReadOnlyList<string> CheckLines { get; } = ["passed · build", "failed · tests: 1 test failed"];

    public string Mergeability => "No conflicts";

    public IReadOnlyList<string> Reviews { get; } = ["lisa approved"];

    public IReadOnlyList<string> WakeUps { get; } = ["Woke the agent: checks failed, resuming its conversation"];

    public string Notice => string.Empty;

    public IAsyncRelayCommand OpenLinkCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand CheckNowCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);
}
