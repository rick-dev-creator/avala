using System.ComponentModel;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Appearance;
using Avala.Sdk.Regions;
using Avala.Testing;
using Avala.Workbench.Contracts.Presentation;
using Avala.Workbench.Navigation;
using CommunityToolkit.Mvvm.Messaging;

namespace Avala.Workbench.Tests.Navigation;

public sealed class JobRegionsScripts : IDisposable
{
    private readonly Bench bench = new();
    private readonly RegionItem chip = new();
    private readonly RegionItem run = new();
    private readonly RegionItem notice = new();
    private readonly PaneItem pane = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public void EachRegionHoldsItsContributionsInTheirOrder()
    {
        var regions = Regions(new RememberedLayout());

        Assert.Equal([chip, run], regions.Header);
        Assert.Equal([notice], regions.Conversation);
        Assert.Equal([pane], regions.Preview);
    }

    [Fact]
    public async Task SelectingAJobHandsItToTheItemsOfEveryRegionAsync()
    {
        var job = bench.Job("Preview the orders page", JobStatus.Running);
        bench.Publish(Bench.OnBoard(job));
        var workbench = bench.Workbench(bench.Ui, Regions(new RememberedLayout()));

        await bench.Ui.InvokeAsync(() => bench.Messenger.Send(new JobSelected(job.Job)), Cancellation);

        Assert.All([chip.Context, run.Context, notice.Context, pane.Context], context => Assert.Equal(Option<JobId>.Some(job.Job), context));
        Assert.Same(workbench.Regions, await bench.Ui.ReadAsync(() => workbench.Regions));
    }

    [Fact]
    public void ActivatingTheJobsPageActivatesTheItemsOfItsRegionsOnceEach()
    {
        var workbench = bench.Workbench(bench.Ui, Regions(new RememberedLayout()));

        workbench.Activate();

        Assert.All([chip.Activations, run.Activations, notice.Activations, pane.Activations], activations => Assert.Equal(1, activations));
        Assert.All([chip.Context, run.Context, notice.Context, pane.Context], context => Assert.Equal(Option<JobId>.None, context));
    }

    [Fact]
    public void ThePreviewShowsWhileItsPaneIsOpen()
    {
        var regions = Regions(new RememberedLayout());

        pane.IsOpen = true;
        Assert.True(regions.IsPreviewOpen);

        pane.IsOpen = false;
        Assert.False(regions.IsPreviewOpen);
    }

    [Fact]
    public async Task ThePreviewTakesItsRememberedShareOfTheWidthAsync()
    {
        var regions = Regions(new RememberedLayout(WindowLayout.Default with { PreviewShare = 0.5 }));
        regions.Activate();
        await regions.Restoring;

        regions.FitCommand.Execute(1_000.0);

        Assert.Equal(500, regions.PreviewWidth);
    }

    [Fact]
    public async Task DraggingThePreviewKeepsItsShareWithinBoundsAndRemembersItOnceTheDragEndsAsync()
    {
        var layout = new RememberedLayout(new WindowLayout(true, 312));
        var regions = Regions(layout);
        regions.FitCommand.Execute(1_000.0);

        regions.ResizePreviewCommand.Execute(900.0);
        Assert.Equal((700, 312.0), (regions.PreviewWidth, layout.Remembered.SidebarWidth));
        Assert.Equal(WindowLayout.DefaultPreviewShare, layout.Remembered.PreviewShare);

        regions.ResizePreviewCommand.Execute(600.0);
        regions.RememberPreviewCommand.Execute(null);
        await regions.Remembering;

        Assert.Equal(600, regions.PreviewWidth);
        Assert.Equal(new WindowLayout(true, 312) { PreviewShare = 0.6 }, layout.Remembered);
    }

    [Fact]
    public async Task ResettingThePreviewReturnsItToFortyFivePercentAsync()
    {
        var layout = new RememberedLayout(WindowLayout.Default with { PreviewShare = 0.6 });
        var regions = Regions(layout);
        regions.FitCommand.Execute(1_000.0);

        regions.ResetPreviewWidthCommand.Execute(null);
        await regions.Remembering;

        Assert.Equal((450, WindowLayout.DefaultPreviewShare), (regions.PreviewWidth, layout.Remembered.PreviewShare));
    }

    public void Dispose() => bench.Dispose();

    private JobRegions Regions(IWindowLayout layout) =>
        new(
            [
                new RegionContribution(WorkbenchRegions.JobHeader, 10, run),
                new RegionContribution(WorkbenchRegions.Preview, 0, pane),
                new RegionContribution(WorkbenchRegions.JobHeader, 0, chip),
                new RegionContribution(WorkbenchRegions.Conversation, 0, notice),
            ],
            layout);

    private sealed class RegionItem : IRegionAware<JobId>, IActivatable
    {
        public Option<JobId> Context { get; private set; }

        public int Activations { get; private set; }

        public void OnRegionContextChanged(Option<JobId> context) => Context = context;

        public void Activate() => Activations++;

        public void Deactivate() => Activations--;
    }

    private sealed class PaneItem : IRegionAware<JobId>, IActivatable, IPane, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public Option<JobId> Context { get; private set; }

        public int Activations { get; private set; }

        public void OnRegionContextChanged(Option<JobId> context) => Context = context;

        public void Activate() => Activations++;

        public void Deactivate() => Activations--;

        public bool IsOpen
        {
            get;
            set
            {
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsOpen)));
            }
        }
    }
}
