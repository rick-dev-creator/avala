using Avala.Components.Privacy;
using Avala.Sdk.Appearance;
using Avala.Testing;
using Avala.Testing.UI;
using Avala.Workbench.Settings;

namespace Avala.Workbench.Tests.Views;

public sealed class AppearanceViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static AppearanceViewModel Settings(FakeAppearance store) => new(store, new PersonalDetailsViewModel(store, InlineUiDispatcher.Instance));

    [Fact]
    public Task TheSectionShowsTheChosenThemeTheChosenMotionAndTheFileAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignAppearanceViewModel());

            Assert.Equal("appearance.json: Applied", view.TextOf("AppearanceFile"));
            Assert.Equal((true, false, false), (view.HasClass("FollowSystem", "selected"), view.HasClass("ChooseLight", "selected"), view.HasClass("ChooseDark", "selected")));
            Assert.Equal((true, false, false), (view.HasClass("MotionFollowSystem", "selected"), view.HasClass("ReduceMotion", "selected"), view.HasClass("FullMotion", "selected")));
            Assert.False(view.Shows("AppearanceError"));
        }, Cancellation);

    [Fact]
    public Task ClickingLightThenReduceMotionOnChangesTheAppearanceAsync() =>
        ui.RunAsync(async () =>
        {
            var store = new FakeAppearance();
            var appearance = Settings(store);
            await appearance.LoadAsync(Cancellation);
            var view = Screen.Show(appearance);

            view.Click("ChooseLight");
            view.Click("ReduceMotion");
            view.Settle();

            Assert.Equal([new AppearancePreference(ThemeChoice.Light, MotionChoice.System), new AppearancePreference(ThemeChoice.Light, MotionChoice.Reduced)], store.Changes);
            Assert.Equal((false, true, true, false), (view.HasClass("FollowSystem", "selected"), view.HasClass("ChooseLight", "selected"), view.HasClass("ReduceMotion", "selected"), view.HasClass("MotionFollowSystem", "selected")));
        }, Cancellation);

    [Fact]
    public Task ClickingWideWidensTheReadingAndKeepsTheThemeAndMotionAsync() =>
        ui.RunAsync(async () =>
        {
            var store = new FakeAppearance(new AppearancePreference(ThemeChoice.Dark, MotionChoice.Reduced));
            var appearance = Settings(store);
            await appearance.LoadAsync(Cancellation);
            var view = Screen.Show(appearance);

            view.Click("ReadWide");
            view.Settle();

            Assert.Equal([new AppearancePreference(ThemeChoice.Dark, MotionChoice.Reduced, ReadingChoice.Wide)], store.Changes);
            Assert.Equal((false, true), (view.HasClass("ReadComfortably", "selected"), view.HasClass("ReadWide", "selected")));
        }, Cancellation);

    [Fact]
    public Task TheSendWithCommandEnterSwitchKeepsTheChoiceAsync() =>
        ui.RunAsync(async () =>
        {
            var store = new FakeAppearance();
            var appearance = Settings(store);
            await appearance.LoadAsync(Cancellation);
            var view = Screen.Show(appearance, Avalonia.Styling.ThemeVariant.Dark, 900, 700);

            view.Click("SendWithCommandEnter");
            await (appearance.ToggleSendKeyCommand.ExecutionTask ?? Task.CompletedTask);
            view.Settle();

            Assert.Equal([AppearancePreference.Default with { Send = SendKey.CommandEnter }], store.Changes);
            Assert.True(view.Find<Avalonia.Controls.ToggleSwitch>("SendWithCommandEnter").IsChecked);
            Assert.Equal("Send with Ctrl+Enter", view.TextOf("SendWithLabel"));
        }, Cancellation);

    [Fact]
    public Task AnErrorIsShownAboveTheChoicesAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignAppearanceViewModel { Error = "appearance.json could not be written, so the change was not kept." });

            Assert.True(view.Shows("AppearanceError"));
            Assert.Contains("appearance.json could not be written, so the change was not kept.", view.VisibleTexts);
        }, Cancellation);

    [Fact]
    public Task TheHidePersonalDetailsSwitchIsOnByDefaultAndTurningItOffRevealsThemAsync() =>
        ui.RunAsync(async () =>
        {
            var store = new FakeAppearance();
            var appearance = Settings(store);
            await appearance.LoadAsync(Cancellation);
            var view = Screen.Show(appearance, Avalonia.Styling.ThemeVariant.Dark, 900, 700);
            var onAtFirst = view.Find<Avalonia.Controls.ToggleSwitch>("HidePersonalDetails").IsChecked;

            view.Click("HidePersonalDetails");
            await (appearance.PersonalDetails.ToggleCommand.ExecutionTask ?? Task.CompletedTask);
            view.Settle();

            Assert.True(onAtFirst);
            Assert.Equal([AppearancePreference.Default with { HidesPersonalDetails = false }], store.Changes);
            Assert.False(view.Find<Avalonia.Controls.ToggleSwitch>("HidePersonalDetails").IsChecked);
        }, Cancellation);
}
