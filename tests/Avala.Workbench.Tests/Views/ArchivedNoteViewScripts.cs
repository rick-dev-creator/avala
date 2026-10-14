using Avala.Testing.UI;
using Avala.Workbench.Archive;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Tests.Views;

public sealed class ArchivedNoteViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task AnArchivedConversationShowsItsChipAndRestoresFromItsGlyphAsync() =>
        ui.RunAsync(() =>
        {
            var restored = 0;
            var view = Screen.Show(new DesignArchivedNoteViewModel
            {
                RestoreCommand = new AsyncRelayCommand(() =>
                {
                    restored++;

                    return Task.CompletedTask;
                }),
            });

            view.Click("Restore");

            Assert.Equal((true, 1), (view.Shows("Chip"), restored));
        }, Cancellation);

    [Fact]
    public Task AConversationThatIsNotArchivedShowsNoNoteAsync() =>
        ui.RunAsync(() => Assert.False(Screen.Show(new DesignArchivedNoteViewModel { IsShown = false }).Shows("Chip")), Cancellation);
}
