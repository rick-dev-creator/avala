using Avala.Testing.UI;
using Avala.Workbench.Notes;
using Avalonia.Controls;
using Avalonia.Styling;

namespace Avala.Workbench.Tests.Views;

public sealed class NoteBoardViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task TheBoardListsTheNotesUntilOneIsOpenedAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignNoteBoardViewModel(), ThemeVariant.Dark, 420, 900);
            var empty = Screen.Show(new DesignNoteBoardViewModel { Notes = [] }, ThemeVariant.Dark, 420, 900);

            Assert.Equal((true, false), (view.Shows("List"), view.Shows("Editor")));
            Assert.Equal(["Login button overlaps the logo", "Split the checkout into steps", "Cache the exchange rates"], view.All<TextBlock>().Where(text => text.Name == "NoteTitle").Select(text => text.Text));
            Assert.Equal((false, true), (view.Shows("Empty"), empty.Shows("Empty")));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task TheEditorShowsTheNotesTitleBodyAndImagesAndOffersArchiveOnlyForAKeptNoteAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignNoteBoardViewModel { IsEditing = true }, ThemeVariant.Light, 420, 900);
            var fresh = Screen.Show(new DesignNoteBoardViewModel { IsEditing = true, IsNew = true }, ThemeVariant.Light, 420, 900);

            Assert.Equal("Split the checkout into steps", view.Find<TextBox>("TitleField").Text);
            Assert.True(view.Find<TextBox>("BodyField").AcceptsReturn);
            Assert.Equal(2, view.All<Image>().Count(image => image.Name == "Picture"));
            Assert.Equal((true, true, false, false), (view.Shows("Save"), view.Shows("Archive"), view.Shows("List"), fresh.Shows("Archive")));
        }, TestContext.Current.CancellationToken);
}
