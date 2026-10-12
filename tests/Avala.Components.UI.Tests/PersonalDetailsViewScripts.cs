using Avala.Components.Privacy;
using Avala.Components.UI.Privacy;
using Avala.Testing;
using Avala.Testing.UI;
using Avalonia.Controls;

namespace Avala.Components.UI.Tests;

public sealed class PersonalDetailsViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task WhileDetailsAreHiddenTheButtonShowsASlashedEyeAndOffersToShowThemAsync() =>
        ui.RunAsync(() =>
        {
            var view = ViewScript.Show(new DesignPersonalDetailsViewModel());

            Assert.Equal((true, false), (view.Shows("Hidden"), view.Shows("Shown")));
            Assert.Equal("Show personal details", Avalonia.Automation.AutomationProperties.GetName(view.Find("TogglePersonalDetails")));
        }, Cancellation);

    [Fact]
    public Task ClickingTheEyeRevealsPersonalDetailsAsync() =>
        ui.RunAsync(async () =>
        {
            using var dispatcher = new TestUiDispatcher();
            var store = new FakeAppearance();
            var details = new PersonalDetailsViewModel(store, dispatcher);
            var view = ViewScript.Show(details);

            view.Click("TogglePersonalDetails");
            await (details.ToggleCommand.ExecutionTask ?? Task.CompletedTask);
            view.Settle();

            Assert.Equal((false, true), (view.Shows("Hidden"), view.Shows("Shown")));
            Assert.False(Assert.Single(store.Changes).HidesPersonalDetails);
        }, Cancellation);

    [Fact]
    public Task APersonalTextIsMaskedByDefaultAsync() =>
        ui.RunAsync(() =>
        {
            var block = new TextBlock();
            PersonalDetails.SetText(block, Personal.Email("dana@example.com"));
            var view = ViewScript.Show(new StackPanel { Children = { block } }, new object());

            Assert.Equal("d•••••@e••••.com", block.Text);
            Assert.True(PersonalDetails.GetAreHidden(view.Window));
        }, Cancellation);

    [Fact]
    public Task RevealingOnTheWindowShowsEveryPersonalTextLiveAndHidingMasksItAgainAsync() =>
        ui.RunAsync(() =>
        {
            var login = new TextBlock();
            var phrase = new TextBlock();
            PersonalDetails.SetText(login, Personal.Login("octocat"));
            PersonalDetails.SetText(phrase, new PersonalPhrase("Signed in as ", Personal.Login("octocat")));
            var view = ViewScript.Show(new StackPanel { Children = { login, phrase } }, new object());

            PersonalDetails.SetAreHidden(view.Window, false);
            var revealed = (login.Text, phrase.Text);
            PersonalDetails.SetAreHidden(view.Window, true);

            Assert.Equal(("octocat", "Signed in as octocat"), revealed);
            Assert.Equal(("oc•••••", "Signed in as oc•••••"), (login.Text, phrase.Text));
        }, Cancellation);
}
