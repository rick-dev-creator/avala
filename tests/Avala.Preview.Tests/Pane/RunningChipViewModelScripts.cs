using Avala.Preview.Pane;
using Avala.Testing;
using CommunityToolkit.Mvvm.Messaging;

namespace Avala.Preview.Tests.Pane;

public sealed class RunningChipViewModelScripts : IDisposable
{
    private readonly PaneBench bench = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheChipNamesTheOriginOfTheSelectedJobsRunningPreviewAsync()
    {
        var script = Selected(ChipPlacement.Header).Then(chip => Assert.False(chip.IsShown));

        await script.WhenPresentedAsync(_ => bench.Serve(bench.Job, 24_000), Cancellation);

        script.Then(chip => Assert.Equal((true, "Running · localhost:24000", false), (chip.IsShown, chip.Text, chip.IsUnleased)));
    }

    [Fact]
    public void AListenerOutsideTheLeaseIsLabelledUnleased()
    {
        bench.Serve(bench.Job, 5_173, leased: false);

        Selected(ChipPlacement.Header).Then(chip => Assert.Equal((true, "Running · localhost:5173", "unleased"), (chip.IsShown, chip.Text, chip.Lease)));
    }

    [Fact]
    public async Task TheChipDisappearsOnceTheServerStopsAsync()
    {
        var target = bench.Serve(bench.Job, 24_000);
        var script = Selected(ChipPlacement.Header);

        await script.WhenPresentedAsync(_ => bench.Stop(target), Cancellation);

        script.Then(chip => Assert.False(chip.IsShown));
    }

    [Fact]
    public void OpeningTheChipAsksForTheSelectedJobsPreview()
    {
        bench.Serve(bench.Job, 24_000);
        var asked = new List<PreviewRequested>();
        bench.Messenger.Register<List<PreviewRequested>, PreviewRequested>(asked, (list, message) => list.Add(message));

        Selected(ChipPlacement.Header).Invoke(nameof(IRunningChipViewModel.OpenCommand));

        Assert.Equal([new PreviewRequested(bench.Job)], asked);
    }

    [Fact]
    public void TheConversationsChipStepsAsideWhileThePaneIsOpenAndTheHeadersStays()
    {
        bench.Serve(bench.Job, 24_000);
        var header = Selected(ChipPlacement.Header);
        var conversation = Selected(ChipPlacement.Conversation);

        bench.Messenger.Send(new PreviewPaneChanged(bench.Job, IsOpen: true));
        Assert.Equal((true, false), (header.ViewModel.IsShown, conversation.ViewModel.IsShown));

        bench.Messenger.Send(new PreviewPaneChanged(bench.Job, IsOpen: false));
        Assert.True(conversation.ViewModel.IsShown);
    }

    public void Dispose() => bench.Dispose();

    private ViewModelScript<RunningChipViewModel> Selected(ChipPlacement placement)
    {
        var chip = bench.Chip(placement);
        chip.OnRegionContextChanged(bench.Job);
        chip.Activate();

        return ViewModelScript.Given(chip);
    }
}
