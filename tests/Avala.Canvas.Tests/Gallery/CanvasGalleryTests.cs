using Avala.Agents.Contracts.Events;
using Avala.Canvas.Canvases;
using Avala.Canvas.Contracts;
using Avala.Canvas.Gallery;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Canvas.Tests.Gallery;

public sealed class CanvasGalleryTests
{
    private readonly CanvasGallery gallery = new(Sketch.Offer);
    private readonly Sketch sketch = Sketch.New();

    [Theory]
    [InlineData("image/svg+xml", true)]
    [InlineData("text/markdown; charset=utf-8", true)]
    [InlineData("Text/Markdown", true)]
    [InlineData("text/vnd.mermaid", false)]
    [InlineData("text/html", false)]
    public void ACanvasIsOpenedAsOfferedOnlyInAMediaTypeTheOfferHolds(string mediaType, bool offered)
    {
        var opened = Outcomes.Succeeds(gallery.Open(sketch.Started(mediaType)));

        Assert.Equal(
            new CanvasOpened(sketch.Id, offered ? Option<CanvasError>.None : CanvasError.NotOffered),
            opened);
        Assert.Equal(offered, gallery.Snapshot(sketch.Id).Match(found => found.IsOffered, () => !offered));
    }

    [Fact]
    public void ACanvasStartsOnlyOnce()
    {
        _ = gallery.Open(sketch.Started());
        _ = gallery.Append(sketch.Chunk("<svg/>"));

        Assert.Equal(CanvasError.AlreadyOpen, Outcomes.FailsWith(gallery.Open(sketch.Started("text/markdown"))));
        Assert.Equal(("image/svg+xml", "<svg/>"), gallery.Snapshot(sketch.Id).Match(found => (found.MediaType, found.Content), () => default));
    }

    [Fact]
    public void ContentForAnItemThatNeverStartedAsACanvasIsUnknown()
    {
        Assert.Equal(CanvasError.UnknownCanvas, Outcomes.FailsWith(gallery.Append(sketch.Chunk("Hello"))));
        Assert.Equal(CanvasError.UnknownCanvas, Outcomes.FailsWith(gallery.Close(sketch.Completed())));
        Assert.Empty(gallery.InSession(sketch.Session));
    }

    [Fact]
    public void ASessionListsOnlyItsOwnCanvasesInStartOrderWithTheirCurrentContent()
    {
        var flow = sketch with { Item = new("flow") };
        var stranger = sketch.Elsewhere();
        _ = gallery.Open(flow.Started("text/vnd.mermaid"));
        _ = gallery.Open(stranger.Started());
        _ = gallery.Open(sketch.Started());
        _ = gallery.Append(flow.Chunk("flowchart LR\n"));
        _ = gallery.Close(flow.Completed());
        _ = gallery.Append(sketch.Chunk("<svg>"));

        Assert.Equal(
            [
                new CanvasSnapshot(flow.Id, sketch.Session, "Architecture", "text/vnd.mermaid", "flowchart LR\n", CanvasStatus.Completed, false),
                new CanvasSnapshot(sketch.Id, sketch.Session, "Architecture", "image/svg+xml", "<svg>", CanvasStatus.Streaming, true),
            ],
            gallery.InSession(sketch.Session));
    }
}
