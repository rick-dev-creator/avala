using Avala.Testing.UI;
using Avala.Workbench.Notes;

namespace Avala.Workbench.Tests.Views;

public sealed class NoteRowViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task ARowShowsTheTitleTimeExcerptAndImageCountAndNoImageLineWithoutImagesAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignNoteRowViewModel());
            var bare = Screen.Show(new DesignNoteRowViewModel("Plan", "2 h ago", "Split the form.", string.Empty) { IsSelected = true });

            Assert.Equal(
                ("Login button overlaps the logo", "12 min ago", "On narrow windows the button sits on top of the logo.", "1 image"),
                (view.TextOf("NoteTitle"), view.TextOf("When"), view.TextOf("Excerpt"), view.TextOf("ImageCount")));
            Assert.False(bare.Shows("ImageCount"));
            Assert.True(bare.HasClass("Note", "selected"));
        }, TestContext.Current.CancellationToken);
}
