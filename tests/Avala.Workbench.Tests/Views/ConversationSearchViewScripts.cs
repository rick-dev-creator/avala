using Avala.Components.UI.Copying;
using Avala.Testing.UI;
using Avala.Workbench.Conversation;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Tests.Views;

public sealed class ConversationSearchViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task TheFindBarShowsWhereTheCurrentMatchIsAndStepsWithEnterAndShiftEnterAsync() =>
        ui.RunAsync(() =>
        {
            var calls = new List<string>();
            var view = Screen.Show(new DesignConversationSearchViewModel
            {
                IsOpen = true,
                NextCommand = new RelayCommand(() => calls.Add("next")),
                PreviousCommand = new RelayCommand(() => calls.Add("previous")),
                CloseCommand = new RelayCommand(() => calls.Add("close")),
            });
            view.Find("Query").Focus();

            view.Press(Key.Enter).Press(Key.Enter, RawInputModifiers.Shift).Press(Key.Escape);

            Assert.Equal(("2 of 5", false), (view.TextOf("Status"), view.HasClass("Status", "none")));
            Assert.Equal(["next", "previous", "close"], calls);
        }, Cancellation);

    [Fact]
    public Task AClosedBarIsHiddenAndATextFoundNowhereIsMarkedAsync() =>
        ui.RunAsync(() =>
        {
            var closed = Screen.Show(new DesignConversationSearchViewModel());
            var nowhere = Screen.Show(new DesignConversationSearchViewModel { IsOpen = true, Status = "No matches", HasMatches = false });

            Assert.False(closed.Shows("Bar"));
            Assert.True(nowhere.HasClass("Status", "none"));
        }, Cancellation);

    [Fact]
    public Task CommandFInTheConversationOpensTheFindBarAsync() =>
        ui.RunAsync(() =>
        {
            var opened = 0;
            var view = Screen.Show(new DesignConversationViewModel { Search = new DesignConversationSearchViewModel { OpenCommand = new RelayCommand(() => opened++) } });

            view.Find("Draft").Focus();
            view.Press(Hotkeys.Find);

            Assert.Equal(1, opened);
        }, Cancellation);

    [Fact]
    public Task AFoundRowIsTintedAndTheCurrentOneMoreAsync() =>
        ui.RunAsync(() =>
        {
            var found = Screen.Show(new DesignMessageViewModel("Refunds round to whole yen", false) { Found = FindMark.Match });
            var current = Screen.Show(new DesignMessageViewModel("Refunds round to whole yen", false) { Found = FindMark.Current });

            var match = found.Named("MessageView").Single();
            var now = current.Named("MessageView").Single();

            Assert.Equal((true, false), (match.Classes.Contains("found"), match.Classes.Contains("current")));
            Assert.Equal((true, true), (now.Classes.Contains("found"), now.Classes.Contains("current")));
        }, Cancellation);
}
