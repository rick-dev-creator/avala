using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Canvas.Canvases;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Canvas.Tests.Canvases;

public sealed class CanvasDocumentTests
{
    private readonly Sketch sketch = Sketch.New();

    [Fact]
    public void OpeningACanvasStartsItStreamingAndEmpty()
    {
        var document = Outcomes.Succeeds(CanvasDocument.Open(sketch.Started("text/markdown"), offered: true));

        Assert.Equal(
            (sketch.Id, sketch.Session, "Architecture", "text/markdown", CanvasState.Streaming, string.Empty, true, Option<CanvasError>.None),
            (document.Id, document.Session, document.Title, document.MediaType, document.State, document.Content, document.IsOffered, document.Rejection));
    }

    [Fact]
    public void ACanvasInAMediaTypeThatIsNotOfferedIsRejectedAsNotOfferedAndKeptAsSource()
    {
        var document = Outcomes.Succeeds(CanvasDocument.Open(sketch.Started("text/vnd.mermaid"), offered: false));

        _ = Outcomes.Succeeds(document.Append(sketch.Chunk("flowchart LR\n")));

        Assert.Equal(
            (false, Option<CanvasError>.Some(CanvasError.NotOffered), "text/vnd.mermaid", "flowchart LR\n"),
            (document.IsOffered, document.Rejection, document.MediaType, document.Content));
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("  ", false)]
    public void ACanvasWithoutAMediaTypeIsRejected(string mediaType, bool offered) =>
        Assert.Equal(CanvasError.MissingMediaType, Outcomes.FailsWith(CanvasDocument.Open(sketch.Started(mediaType), offered)));

    [Fact]
    public void ChunksAccumulateInTheOrderTheyArrive()
    {
        var document = Opened();

        string[] chunks = ["<svg>", "<rect/>", "</svg>"];

        var appended = chunks.Select(chunk => Outcomes.Succeeds(document.Append(sketch.Chunk(chunk))));

        Assert.Equal([5, 12, 18], appended.Select(update => update.Length));
        Assert.Equal("<svg><rect/></svg>", document.Content);
    }

    [Fact]
    public void ContentAndCompletionOfAnotherItemAreRejected()
    {
        var document = Opened();
        Sketch[] strangers =
        [
            sketch with { Session = SessionId.New() },
            sketch with { Turn = TurnId.New() },
            sketch with { Item = new ItemId("other") },
        ];

        Assert.All(strangers, stranger =>
        {
            Assert.Equal(CanvasError.ForeignItem, Outcomes.FailsWith(document.Append(stranger.Chunk("<svg>"))));
            Assert.Equal(CanvasError.ForeignItem, Outcomes.FailsWith(document.Close(stranger.Completed())));
        });
        Assert.Equal((CanvasState.Streaming, string.Empty), (document.State, document.Content));
    }

    [Fact]
    public void EveryOutcomeClosesTheCanvasInItsOwnState()
    {
        var closings = Enum.GetValues<ItemOutcome>().Select(outcome =>
        {
            var document = Opened();
            var closed = Outcomes.Succeeds(document.Close(sketch.Completed(outcome)));

            return (outcome, closed.State, document.State);
        });

        Assert.Equal(
            [
                (ItemOutcome.Succeeded, CanvasState.Completed, CanvasState.Completed),
                (ItemOutcome.Failed, CanvasState.Failed, CanvasState.Failed),
                (ItemOutcome.Cancelled, CanvasState.Cancelled, CanvasState.Cancelled),
                (ItemOutcome.Abandoned, CanvasState.Abandoned, CanvasState.Abandoned),
                (ItemOutcome.Expired, CanvasState.Expired, CanvasState.Expired),
            ],
            closings);
    }

    [Fact]
    public void ContentAfterCompletionIsRejected()
    {
        var document = Opened();
        _ = document.Append(sketch.Chunk("<svg/>"));
        _ = document.Close(sketch.Completed());

        Assert.Equal(CanvasError.AlreadyClosed, Outcomes.FailsWith(document.Append(sketch.Chunk("<late/>"))));
        Assert.Equal("<svg/>", document.Content);
    }

    [Fact]
    public void ACanvasClosesOnlyOnce()
    {
        var document = Opened();
        _ = document.Close(sketch.Completed(ItemOutcome.Failed));

        Assert.Equal(CanvasError.AlreadyClosed, Outcomes.FailsWith(document.Close(sketch.Completed())));
        Assert.Equal(CanvasState.Failed, document.State);
    }

    private CanvasDocument Opened() => Outcomes.Succeeds(CanvasDocument.Open(sketch.Started(), offered: true));
}
