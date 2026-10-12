using Avala.Workbench.ModelChoices;
using Avala.Workbench.Presenting;
using Avala.Workbench.RepositoryChoice;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.NewJob;

internal sealed record DesignConnectionOptionViewModel(string Name, string Reading, bool IsNearLimit) : IConnectionOptionViewModel
{
    public DesignConnectionOptionViewModel()
        : this("claude-work", "88% of the 5-hour window used", true)
    {
    }
}

[INotifyPropertyChanged]
internal sealed partial class DesignNewJobViewModel : INewJobViewModel
{
    public string Title => "New job";

    public IRepositoryFieldViewModel RepositoryField { get; } = new DesignRepositoryFieldViewModel
    {
        Repository = "/home/ada/code/shop-api",
        Problem = string.Empty,
        Offer = string.Empty,
        Creation = new DesignNewRepositoryViewModel { IsOpen = false },
    };

    public IRepositoryPickerViewModel RepositoryPicker { get; } = new DesignRepositoryPickerViewModel();

    public IReadOnlyList<string> Connections { get; } = [NewJobPhrases.Auto, "claude-work", "claude-personal"];

    public IReadOnlyList<IConnectionOptionViewModel> Options { get; } =
    [
        new DesignConnectionOptionViewModel(NewJobPhrases.Auto, string.Empty, false),
        new DesignConnectionOptionViewModel("claude-work", "88% of the 5-hour window used", true),
        new DesignConnectionOptionViewModel("claude-personal", "31% of the 5-hour window used", false),
    ];

    [ObservableProperty]
    public partial string Instruction { get; set; } = "Add an invoice PDF endpoint that serves the rendered invoice with a cache header.";

    [ObservableProperty]
    public partial string Connection { get; set; } = NewJobPhrases.Auto;

    public string Route => "Auto → claude-personal · 31% of the 5-hour window used · the most capacity left";

    public bool IsRouteAttention => false;

    public IReadOnlyList<string> Autonomies { get; } = ["Repository's level: autonomous", NewJobPhrases.Supervised];

    [ObservableProperty]
    public partial string Autonomy { get; set; } = "Repository's level: autonomous";

    public string AutonomyNote => "Autonomous, as the repository's .avala/permissions.json declares: edits and commands inside the worktree run without asking, anything else is denied, forms are answered by policy.";

    public IJobOptionsViewModel JobOptions { get; } = new DesignJobOptionsViewModel();

    public string Error => string.Empty;

    public string Submitted => "Submitted: Fix JPY rounding in invoice totals";

    public IAsyncRelayCommand SubmitCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand StartWithoutRepositoryCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);
}

[INotifyPropertyChanged]
internal sealed partial class DesignJobOptionsViewModel : IJobOptionsViewModel
{
    public IModelPickerViewModel Models { get; } = new DesignModelPickerViewModel();

    public int Candidates { get; init; } = 3;

    public string CandidatesText => ContestPhrases.Candidates(Candidates);

    public bool OffersContest => true;

    public bool IsContest => Candidates > 1;

    [ObservableProperty]
    public partial bool Spread { get; set; }

    public string SpreadNote => ContestPhrases.SpreadNote;

    public string Share => ContestPhrases.Share(Candidates, new Budgets.Contracts.BudgetCaps([new Agents.Contracts.Events.Cost(5.00m, "USD")], Sdk.Option<long>.None, Sdk.Option<double>.None));

    public string Note => ContestPhrases.BestOf(new Contests.Contracts.BestOf(3, 4));

    public IRelayCommand MoreCommand { get; } = new RelayCommand(() => { });

    public IRelayCommand FewerCommand { get; } = new RelayCommand(() => { });
}
