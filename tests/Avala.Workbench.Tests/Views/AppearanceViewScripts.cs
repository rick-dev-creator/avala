using Avala.Sdk.Appearance;
using Avala.Testing.UI;
using Avala.Workbench.Settings;

namespace Avala.Workbench.Tests.Views;

public sealed class AppearanceViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

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
            var appearance = new AppearanceViewModel(store);
            await appearance.LoadAsync(Cancellation);
            var view = Screen.Show(appearance);

            view.Click("ChooseLight");
            view.Click("ReduceMotion");
            view.Settle();

            Assert.Equal([new AppearancePreference(ThemeChoice.Light, MotionChoice.System), new AppearancePreference(ThemeChoice.Light, MotionChoice.Reduced)], store.Changes);
            Assert.Equal((false, true, true, false), (view.HasClass("FollowSystem", "selected"), view.HasClass("ChooseLight", "selected"), view.HasClass("ReduceMotion", "selected"), view.HasClass("MotionFollowSystem", "selected")));
        }, Cancellation);

    [Fact]
    public Task AnErrorIsShownAboveTheChoicesAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignAppearanceViewModel { Error = "appearance.json could not be written, so the change was not kept." });

            Assert.True(view.Shows("AppearanceError"));
            Assert.Contains("appearance.json could not be written, so the change was not kept.", view.VisibleTexts);
        }, Cancellation);
}
