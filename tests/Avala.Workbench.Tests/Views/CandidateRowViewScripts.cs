using Avala.Components.Status;
using Avala.Testing.UI;
using Avala.Workbench.Presenting;
using Avala.Workbench.Sidebar;

namespace Avala.Workbench.Tests.Views;

public sealed class CandidateRowViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task ACandidateRowShowsItsOrdinalAndWhatItsJobIsDoingAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignCandidateRowViewModel(SampleJobs.LodashUpdate, 3, "needs help: retries ran out", StatusKind.NeedsYou));

            Assert.Equal(("Candidate 3", "needs help: retries ran out", false), (view.TextOf("Title"), view.TextOf("Fact"), view.Shows("PendingBadge")));
        }, TestContext.Current.CancellationToken);
}
