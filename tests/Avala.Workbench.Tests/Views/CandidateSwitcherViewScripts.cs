using Avala.Testing.UI;
using Avala.Workbench.Review;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Tests.Views;

public sealed class CandidateSwitcherViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task TheSwitcherShowsEachCandidateWithItsFactsAndMarksTheOpenOneAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignCandidateSwitcherViewModel());
            var choices = view.All<Button>().Where(button => button.Classes.Contains("choice")).ToList();

            Assert.Equal("3 candidates · Decided: the winner waits for your review", view.TextOf("Heading"));
            Assert.Equal([false, false, true], choices.Select(choice => choice.Classes.Contains("open")));
            Assert.Contains("verified · 1 line changed · 0.009 USD", view.VisibleTexts);
        }, Cancellation);

    [Fact]
    public Task ClickingACandidateRunsTheSwitchWithItAsync() =>
        ui.RunAsync(() =>
        {
            var switched = new List<CandidateChoice>();
            var design = new DesignCandidateSwitcherViewModel();
            var view = Screen.Show(new Switching(design, new RelayCommand<object>(choice => switched.Add((CandidateChoice)choice!))));

            view.Click(view.All<Button>().First(button => button.Classes.Contains("choice")));

            Assert.Equal([design.Choices[0]], switched);
        }, Cancellation);

    private sealed record Switching(DesignCandidateSwitcherViewModel Design, IRelayCommand<object> SwitchCommand) : ICandidateSwitcherViewModel
    {
        public bool IsShown => true;

        public string Heading => Design.Heading;

        public IReadOnlyList<CandidateChoice> Choices => Design.Choices;
    }
}
