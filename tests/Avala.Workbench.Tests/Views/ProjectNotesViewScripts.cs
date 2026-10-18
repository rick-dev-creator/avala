using Avala.Testing.UI;
using Avala.Workbench.Notes;
using Avalonia.Controls;
using Avalonia.Styling;

namespace Avala.Workbench.Tests.Views;

public sealed class ProjectNotesViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task TheProjectsNotesPageNamesTheProjectAboveItsNotesAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignProjectNotesViewModel(), ThemeVariant.Dark, 900, 700);

            Assert.Equal(("Notes", "acme-dashboard"), (view.TextOf("PageTitle"), view.TextOf("Where")));
            Assert.True(view.Shows("NewNote"));
            Assert.Equal(3, view.All<TextBlock>().Count(text => text.Name == "NoteTitle" && text.IsEffectivelyVisible));
        }, TestContext.Current.CancellationToken);
}
