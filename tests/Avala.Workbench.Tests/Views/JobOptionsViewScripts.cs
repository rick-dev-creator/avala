using Avala.Testing.UI;
using Avala.Workbench.ModelChoices;
using Avala.Workbench.NewJob;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Tests.Views;

public sealed partial class JobOptionsViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task AContestShowsItsCandidatesTheSpreadToggleAndEachCandidatesShareAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignJobOptionsViewModel());

            Assert.Equal(("3 candidates", "Each candidate may spend a third of a job's budget: 1.67 USD."), (view.TextOf("Candidates"), view.TextOf("Share")));
            Assert.Equal((true, true, false), (view.Shows("Spread"), view.Shows("Models"), view.Shows("SpreadNote")));
            Assert.StartsWith("Up to 4 candidates", view.TextOf("ContestNote"), StringComparison.Ordinal);
        }, Cancellation);

    [Fact]
    public Task OneCandidateHidesTheSpreadToggleAndTheShareAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignJobOptionsViewModel { Candidates = 1 });

            Assert.Equal(("1 candidate", false, false), (view.TextOf("Candidates"), view.Shows("Spread"), view.Shows("Share")));
        }, Cancellation);

    [Fact]
    public Task ClickingMoreAndFewerRunsTheirCommandsAndCheckingSpreadExplainsItAsync() =>
        ui.RunAsync(() =>
        {
            var options = new CountingOptions();
            var view = Screen.Show(options);

            view.Click("More").Click("More").Click("Fewer").Click("Spread");

            Assert.Equal((2, 1, true), (options.Added, options.Removed, options.Spread));
            Assert.True(view.Settle().Shows("SpreadNote"));
        }, Cancellation);

    [INotifyPropertyChanged]
    private sealed partial class CountingOptions : IJobOptionsViewModel
    {
        public CountingOptions()
        {
            MoreCommand = new RelayCommand(() => Added++);
            FewerCommand = new RelayCommand(() => Removed++);
        }

        public int Added { get; private set; }

        public int Removed { get; private set; }

        public IModelPickerViewModel Models { get; } = new DesignModelPickerViewModel();

        public int Candidates => 2;

        public string CandidatesText => "2 candidates";

        public bool OffersContest => true;

        public bool IsContest => true;

        [ObservableProperty]
        public partial bool Spread { get; set; }

        public string SpreadNote => "Each candidate runs on the next usable connection in turn.";

        public string Share => string.Empty;

        public string Note => string.Empty;

        public IRelayCommand MoreCommand { get; }

        public IRelayCommand FewerCommand { get; }
    }
}
