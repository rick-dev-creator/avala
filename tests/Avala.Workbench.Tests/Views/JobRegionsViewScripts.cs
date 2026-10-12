using System.ComponentModel;
using Avala.Jobs.Contracts;
using Avala.Sdk.Regions;
using Avala.Testing;
using Avala.Testing.UI;
using Avala.Workbench.Contracts.Presentation;
using Avala.Workbench.Navigation;
using CommunityToolkit.Mvvm.Messaging;

namespace Avala.Workbench.Tests.Views;

public sealed class JobRegionsViewScripts(HeadlessUi ui)
{
    private const string Chip = "Running · localhost:24000";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task TheJobsHeaderShowsItsRegionAndAnOpenPreviewSitsBesideTheConversationAtItsShareAsync() =>
        ui.RunAsync(() =>
        {
            using var bench = new Bench();
            var pane = new Pane();
            var page = Opened(bench, pane);
            var view = Screen.Show(page);
            Assert.Contains(Chip, view.VisibleTexts);
            Assert.False(view.Shows("PreviewPane"));

            pane.IsOpen = true;
            view.Settle();

            Assert.True(view.Shows("PreviewPane"));
            Assert.Contains("orders page", view.VisibleTexts);
            Assert.Equal(Math.Round(view.Find("Split").Bounds.Width * 0.45), view.Find("PreviewPane").Bounds.Width);
            page.Deactivate();
        }, Cancellation);

    [Fact]
    public Task DraggingThePreviewsEdgeWidensItAsync() =>
        ui.RunAsync(() =>
        {
            using var bench = new Bench();
            var pane = new Pane { IsOpen = true };
            var page = Opened(bench, pane);
            var view = Screen.Show(page);
            var before = view.Find("PreviewPane").Bounds.Width;

            view.Drag("PreviewGrip", -60);

            Assert.Equal(before + 60, view.Find("PreviewPane").Bounds.Width, 1);
            page.Deactivate();
        }, Cancellation);

    [Fact]
    public Task FocusModeHidesThePreviewAsync() =>
        ui.RunAsync(() =>
        {
            using var bench = new Bench();
            var page = Opened(bench, new Pane { IsOpen = true });
            var view = Screen.Show(page);

            Components.UI.Theme.Chrome.SetIsFocusMode(view.Window, true);
            view.Settle();

            Assert.False(view.Shows("PreviewPane"));
            page.Deactivate();
        }, Cancellation);

    private static WorkbenchViewModel Opened(Bench bench, Pane pane)
    {
        var regions = new JobRegions(
            [new RegionContribution(WorkbenchRegions.JobHeader, 0, Chip), new RegionContribution(WorkbenchRegions.Preview, 0, pane)],
            new RememberedLayout());
        var page = bench.Workbench(bench.Ui, regions);
        var job = bench.Job("Preview the orders page", JobStatus.Running);
        page.Activate();
        bench.Messenger.Send(new JobSelected(job.Job));

        return page;
    }

    private sealed class Pane : IPane, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public bool IsOpen
        {
            get;
            set
            {
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsOpen)));
            }
        }

        public override string ToString() => "orders page";
    }
}
