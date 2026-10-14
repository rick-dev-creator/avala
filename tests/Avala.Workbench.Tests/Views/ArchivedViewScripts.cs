using Avala.Testing.UI;
using Avala.Workbench.Archive;
using Avalonia.Controls;

namespace Avala.Workbench.Tests.Views;

public sealed class ArchivedViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task ThePageListsEachProjectsArchivedConversationsWithTheirStatusAndWhenTheyEndedAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignArchivedViewModel());
            var texts = view.All<TextBlock>().Select(text => text.Text).ToHashSet();

            Assert.Equal(("Archived", "4 conversations, kept with their transcripts and records", false), (view.TextOf("Title"), view.TextOf("Summary"), view.Shows("Empty")));
            Assert.Superset(new HashSet<string?> { "acme-dashboard", "Chats", "Fix JPY rounding in invoice totals", "Approved", "Yesterday", "Ready for review" }, texts);
        }, Cancellation);

    [Fact]
    public Task WithNothingArchivedThePageShowsItsEmptyStateAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignArchivedViewModel { HasArchived = false, Groups = [] });

            Assert.Equal((true, false), (view.Shows("Empty"), view.Shows("Groups")));
        }, Cancellation);
}
