using Avala.Testing.UI;
using Avala.Workbench.Settings;
using Avalonia.Controls;
using Avalonia.Styling;

namespace Avala.Workbench.Tests.Views;

public sealed class WorktreeSettingsViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task TheSectionShowsAChoiceForEachMomentAndEachDeletionAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignWorktreeSettingsViewModel(), ThemeVariant.Dark, 720, 900);

            Assert.Equal(11, view.All<ComboBox>().Count(choice => choice.Name == "Choices"));
            Assert.Equal("resources.json: Absent", view.TextOf("File"));
            Assert.False(view.Find<Button>("Save").IsVisible);
        }, Cancellation);

    [Fact]
    public Task AChangedDraftOffersSaveAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignWorktreeSettingsViewModel { IsChanged = true }, ThemeVariant.Dark, 720, 900);

            Assert.True(view.Find<Button>("Save").IsVisible);
        }, Cancellation);
}
