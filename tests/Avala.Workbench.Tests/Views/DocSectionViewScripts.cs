using Avala.Testing.UI;
using Avala.Workbench.Docs;
using Avalonia.Controls;

namespace Avala.Workbench.Tests.Views;

public sealed class DocSectionViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task ASectionShowsItsTitleTextAndMarkAndStandsOutWhileItsStepPlaysAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignDocSectionViewModel());
            var quiet = Screen.Show(new DesignDocSectionViewModel("merchant", "Merchant", "Nothing changes.", string.Empty) { IsActive = false });

            Assert.Equal(("Payments", "new", true), (view.TextOf("SectionTitle"), view.TextOf("SectionMark"), view.Find<Button>("Section").Classes.Contains("active")));
            Assert.Equal((false, false), (quiet.Shows("SectionMark"), quiet.Find<Button>("Section").Classes.Contains("active")));
        }, Cancellation);
}
