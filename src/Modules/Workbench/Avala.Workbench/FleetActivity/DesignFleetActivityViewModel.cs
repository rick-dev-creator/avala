using Avala.Components.Privacy;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.FleetActivity;

internal sealed class DesignFleetActivityViewModel : IFleetActivityViewModel
{
    public string Notice { get; init; } = string.Empty;

    public bool HasNotice => Notice.Length > 0;

    public IReadOnlyList<IFleetRepositoryRowViewModel> Repositories { get; init; } = [new DesignFleetRepositoryRowViewModel()];

    public string Unattributed { get; init; } = "2 jobs whose repository is not known yet";

    public bool HasUnattributed => Unattributed.Length > 0;

    public IAsyncRelayCommand RefreshCommand { get; init; } = new AsyncRelayCommand(() => Task.CompletedTask);
}

internal sealed class DesignFleetRepositoryRowViewModel : IFleetRepositoryRowViewModel
{
    public Personal Key { get; init; } = Personal.AccountName("remote:github.com/rick/avala");

    public string Summary { get; init; } = "12 jobs on 2 instances · last 25/10/2026 12:01";

    public string Spending { get; init; } = "1,500,000 in, 150,000 out, 9,000,000 cache read · 14.5 USD at API prices";

    public bool IsExpanded { get; init; } = true;

    public string Notice { get; init; } = string.Empty;

    public IReadOnlyList<IFleetJobRowViewModel> Jobs { get; init; } = [new DesignFleetJobRowViewModel()];

    public IAsyncRelayCommand ToggleCommand { get; init; } = new AsyncRelayCommand(() => Task.CompletedTask);
}

internal sealed class DesignFleetJobRowViewModel : IFleetJobRowViewModel
{
    public Personal Instance { get; init; } = Personal.AccountName("laptop");

    public string State { get; init; } = "awaitingReview · 2 attempts, last passed · 25/10/2026 12:01";

    public string Spending { get; init; } = "120,000 in, 18,000 out, 900,000 cache read · 1.2 USD at API prices";
}
