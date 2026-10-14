using Avala.Components.Status;
using Avala.Jobs.Contracts;
using Avala.Testing.UI;
using Avala.Workbench.Archive;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Tests.Views;

public sealed class ArchivedRowViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task ARowShowsItsTitleStatusAndWhenItEndedAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignArchivedRowViewModel());

            Assert.Equal(("Update lodash to 4.17.21", "Approved", "2 d ago"), (view.TextOf("Title"), view.TextOf("Status"), view.TextOf("Ended")));
        }, Cancellation);

    [Fact]
    public Task ARowOpensItsConversationAndItsGlyphRestoresItAsync() =>
        ui.RunAsync(() =>
        {
            var calls = new List<string>();
            var view = Screen.Show(new DesignArchivedRowViewModel(JobId.New(), "Paginate the export", "Approved", "2 d ago", StatusKind.Done)
            {
                OpenCommand = new RelayCommand(() => calls.Add("open")),
                RestoreCommand = new AsyncRelayCommand(() =>
                {
                    calls.Add("restore");

                    return Task.CompletedTask;
                }),
            });

            view.Click("Open").Click("Restore");

            Assert.Equal(["open", "restore"], calls);
        }, Cancellation);
}
