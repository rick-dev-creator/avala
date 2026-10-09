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
}
