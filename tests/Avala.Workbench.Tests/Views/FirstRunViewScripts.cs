using Avala.Sdk;
using Avala.Testing.UI;
using Avala.Workbench.Navigation;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Tests.Views;

public sealed class FirstRunViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task TheGuideSaysThereIsNoConnectionAndHowToGetOneAsync() =>
        ui.RunAsync(() =>
        {
            var opened = 0;
            var view = Screen.Show(new Guide(() => opened++));

            Assert.Equal("No connections yet", view.TextOf("Heading"));
            Assert.StartsWith("A job runs on a harness", view.TextOf("Explanation"), StringComparison.Ordinal);
            Assert.Equal(("Log in to Claude Code", "Or add a connection in Settings"), (view.TextOf("LogIn"), view.TextOf("AddConnection")));

            view.Click("OpenSettings");

            Assert.Equal(1, opened);
        }, Cancellation);

    [Fact]
    public Task TheJobsPageShowsTheGuideInsteadOfAskingToChooseAJobAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignWorkbenchViewModel(Option<Avala.Workbench.Conversation.IConversationViewModel>.None, false) { FirstRun = new DesignFirstRunViewModel() });

            Assert.Equal((true, false), (view.Shows("FirstRun"), view.Shows("NoJobHint")));
            Assert.Contains("No connections yet", view.VisibleTexts);
        }, Cancellation);

    private sealed class Guide(Action open) : IFirstRunViewModel
    {
        public bool IsShown => true;

        public string Heading => "No connections yet";

        public string Explanation => new DesignFirstRunViewModel().Explanation;

        public IRelayCommand OpenSettingsCommand { get; } = new RelayCommand(open);

        public Task CheckAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
