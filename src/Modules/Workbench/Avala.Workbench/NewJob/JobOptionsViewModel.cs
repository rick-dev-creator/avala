using Avala.Budgets.Contracts;
using Avala.Contests.Contracts;
using Avala.Sdk;
using Avala.Workbench.ModelChoices;
using Avala.Workbench.Presenting;
using Avala.Workbench.Submitting;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.NewJob;

internal interface IJobOptionsViewModel
{
    IModelPickerViewModel Models { get; }

    int Candidates { get; }

    string CandidatesText { get; }

    bool OffersContest { get; }

    bool IsContest { get; }

    bool Spread { get; set; }

    string SpreadNote { get; }

    string Share { get; }

    string Note { get; }

    IRelayCommand MoreCommand { get; }

    IRelayCommand FewerCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class JobOptionsViewModel(ModelPickerViewModel models, ContestReadings readings) : IJobOptionsViewModel
{
    private BudgetCaps caps = new([], Option<long>.None, Option<double>.None);
    private int reads;

    public IModelPickerViewModel Models => models;

    public ModelPickerViewModel Picker => models;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CandidatesText), nameof(IsContest))]
    [NotifyCanExecuteChangedFor(nameof(MoreCommand), nameof(FewerCommand))]
    public partial int Candidates { get; private set; } = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OffersContest))]
    [NotifyCanExecuteChangedFor(nameof(MoreCommand), nameof(FewerCommand))]
    public partial int Most { get; private set; } = BestOf.Default.Max;

    public string CandidatesText => ContestPhrases.Candidates(Candidates);

    public bool OffersContest => Most > 1;

    public bool IsContest => Candidates > 1;

    [ObservableProperty]
    public partial bool Spread { get; set; }

    public string SpreadNote => ContestPhrases.SpreadNote;

    [ObservableProperty]
    public partial string Share { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Note { get; private set; } = string.Empty;

    public async Task ReadAsync(string repository, CancellationToken cancellationToken)
    {
        var ticket = ++reads;
        var declared = await readings.BestOfAsync(repository, cancellationToken);
        var capped = await readings.CapsAsync(repository, cancellationToken);

        if (ticket == reads)
        {
            var bestOf = declared.Match(found => found, _ => new BestOf(1, 1));
            caps = capped;
            Most = bestOf.Max;
            Candidates = bestOf.Candidates;
            Note = string.IsNullOrWhiteSpace(repository) ? string.Empty : ContestPhrases.BestOf(declared);
            Share = ContestPhrases.Share(Candidates, caps);
        }
    }

    partial void OnCandidatesChanged(int value) => Share = ContestPhrases.Share(value, caps);

    [RelayCommand(CanExecute = nameof(CanAddCandidate))]
    private void More() => Candidates++;

    [RelayCommand(CanExecute = nameof(CanRemoveCandidate))]
    private void Fewer() => Candidates--;

    private bool CanAddCandidate() => Candidates < Most;

    private bool CanRemoveCandidate() => Candidates > 1;
}
