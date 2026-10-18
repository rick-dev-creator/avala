using Avala.Testing.UI;
using Avala.Workbench.Notes;
using Avalonia.Controls;
using Avalonia.Styling;

namespace Avala.Workbench.Tests.Views;

public sealed class NotesPaneViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task ThePaneHeadsTheJobsNotesWithNewNoteAndCloseAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignNotesPaneViewModel(), ThemeVariant.Dark, 420, 900);

            Assert.Equal((true, true), (view.Shows("NewNote"), view.Shows("Close")));
            Assert.Equal(3, view.All<TextBlock>().Count(text => text.Name == "NoteTitle" && text.IsEffectivelyVisible));
        }, TestContext.Current.CancellationToken);
}
