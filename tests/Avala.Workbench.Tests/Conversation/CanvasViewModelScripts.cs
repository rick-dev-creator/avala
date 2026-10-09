using Avala.Canvas.Contracts;
using Avala.Testing;
using Avala.Workbench.Conversation;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Tests.Conversation;

public sealed class CanvasViewModelScripts
{
    [Fact]
    public void ACanvasShowsItsLatestSnapshotAndStreamsUntilClosed() =>
        ViewModelScript.Given(new CanvasViewModel(new CanvasEntry("c", "Rounding before and after", "text/vnd.mermaid", string.Empty, CanvasStatus.Streaming)))
            .Then(canvas => Assert.True(canvas.IsStreaming))
            .When(canvas => canvas.Update(new CanvasEntry("c", "Rounding before and after", "text/vnd.mermaid", "flowchart LR\n  A --> B", CanvasStatus.Completed)))
            .ThenNotified(nameof(CanvasViewModel.Content), nameof(CanvasViewModel.Status), nameof(CanvasViewModel.IsStreaming))
            .Then(canvas => Assert.Equal(("flowchart LR\n  A --> B", false), (canvas.Content, canvas.IsStreaming)));

    [Fact]
    public void AFailedCanvasStopsStreamingAndKeepsWhatItDrew() =>
        ViewModelScript.Given(new CanvasViewModel(new CanvasEntry("c", "Flow", "text/vnd.mermaid", "flowchart", CanvasStatus.Streaming)))
            .When(canvas => canvas.Update(new CanvasEntry("c", "Flow", "text/vnd.mermaid", "flowchart", CanvasStatus.Failed)))
            .Then(canvas => Assert.Equal((CanvasStatus.Failed, false, "flowchart"), (canvas.Status, canvas.IsStreaming, canvas.Content)));

    [Fact]
    public void ACanvasTheCanvasModuleDidNotOfferReachesItsSurfaceAsNotOffered() =>
        ViewModelScript.Given(new CanvasViewModel(new CanvasEntry("c", "Flow", "image/svg+xml", "<svg/>", CanvasStatus.Streaming)))
            .Then(canvas => Assert.True(canvas.Surface.Shown.IsOffered))
            .When(canvas => canvas.Update(new CanvasEntry("c", "Flow", "text/vnd.mermaid", "flowchart LR", CanvasStatus.Completed) { IsOffered = false }))
            .Then(canvas => Assert.Equal(("text/vnd.mermaid", false), (canvas.Surface.Shown.MediaType, canvas.Surface.Shown.IsOffered)));

    [Theory]
    [InlineData(CanvasStatus.Completed, "")]
    [InlineData(CanvasStatus.Failed, "Failed")]
    [InlineData(CanvasStatus.Cancelled, "Stopped")]
    [InlineData(CanvasStatus.Abandoned, "Stopped")]
    [InlineData(CanvasStatus.Expired, "Stopped")]
    public void ItsSurfaceKeepsEverySnapshotAsAVersionAndShowsHowItClosed(CanvasStatus closed, string status) =>
        ViewModelScript.Given(new CanvasViewModel(new CanvasEntry("c", "Flow", "text/vnd.mermaid", "flowchart", CanvasStatus.Streaming)))
            .Then(canvas => Assert.Equal(("Drawing", "Mermaid"), (canvas.Surface.StatusText, canvas.Surface.MediaLabel)))
            .When(canvas => canvas.Update(new CanvasEntry("c", "Flow", "text/vnd.mermaid", "flowchart LR\n  A --> B", closed)))
            .Then(canvas => Assert.Equal(
                ("Flow", "flowchart LR\n  A --> B", true, "2 of 2", status, false),
                (canvas.Surface.Title, canvas.Surface.Shown.Content, canvas.Surface.Shown.IsFinal, canvas.Surface.VersionText, canvas.Surface.StatusText, canvas.Surface.IsStreaming)));
}
