using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Canvas.Contracts;
using Avala.Canvas.Drawing;

namespace Avala.Canvas.Tests;

internal sealed record Sketch(SessionId Session, TurnId Turn, ItemId Item)
{
    public static CanvasFormat Svg { get; } = new("image/svg+xml", "SVG", "Draw every diagram as SVG.");

    public static CanvasFormat Markdown { get; } = new("text/markdown", "Markdown", "Write notes as Markdown.");

    public static CanvasOffer Offer { get; } = new([Svg, Markdown]);

    public static Sketch New(string item = "diagram") => new(SessionId.New(), TurnId.New(), new ItemId(item));

    public CanvasId Id => new(Turn, Item);

    public CanvasStarted Started(string mediaType = "image/svg+xml") => new(Session, Turn, Item, "Architecture", mediaType);

    public ItemProgressed Chunk(string text) => new(Session, Turn, Item, text);

    public ItemCompleted Completed(ItemOutcome outcome = ItemOutcome.Succeeded) => new(Session, Turn, Item, outcome);

    public Sketch Elsewhere() => this with { Session = SessionId.New(), Turn = TurnId.New() };
}
