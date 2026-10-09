using System.Text;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Canvas.Contracts;
using Avala.Sdk;
using Avala.Sdk.Domain;
using Stateless;

namespace Avala.Canvas.Canvases;

internal sealed class CanvasDocument : IAggregateRoot<CanvasId>
{
    public const string PlainText = "text/plain";

    private readonly StringBuilder content = new();
    private readonly StateMachine<CanvasState, CanvasTrigger> machine;

    private CanvasDocument(CanvasStarted started, bool offered)
    {
        Id = new CanvasId(started.Turn, started.Item);
        Session = started.Session;
        Title = started.Title;
        HasMediaType = !string.IsNullOrWhiteSpace(started.MediaType);
        MediaType = HasMediaType ? started.MediaType : PlainText;
        IsOffered = offered && HasMediaType;
        machine = CanvasLifecycle.Create(() => State, state => State = state);
    }

    public CanvasId Id { get; }

    public SessionId Session { get; }

    public string Title { get; }

    public string MediaType { get; }

    public bool IsOffered { get; }

    public Option<CanvasError> Rejection =>
        !HasMediaType ? CanvasError.MissingMediaType
        : IsOffered ? Option<CanvasError>.None
        : CanvasError.NotOffered;

    public CanvasState State { get; private set; } = CanvasState.Streaming;

    public string Content => content.ToString();

    private bool HasMediaType { get; }

    public static Result<CanvasDocument, CanvasError> Open(CanvasStarted started, bool offered) => new CanvasDocument(started, offered);

    public Result<ContentAppended, CanvasError> Append(ItemProgressed progressed)
    {
        if (!Owns(progressed.Session, progressed.Turn, progressed.Item))
        {
            return CanvasError.ForeignItem;
        }

        if (machine.IsInState(CanvasState.Closed))
        {
            return CanvasError.AlreadyClosed;
        }

        content.Append(progressed.Text);

        return new ContentAppended(Id, content.Length);
    }

    public Result<CanvasClosed, CanvasError> Close(ItemCompleted completed) =>
        Owns(completed.Session, completed.Turn, completed.Item)
            ? machine.TryFire(TriggerOf(completed.Outcome), CanvasError.AlreadyClosed).Map(state => new CanvasClosed(Id, state))
            : CanvasError.ForeignItem;

    private static CanvasTrigger TriggerOf(ItemOutcome outcome) => outcome switch
    {
        ItemOutcome.Failed => CanvasTrigger.Fail,
        ItemOutcome.Cancelled => CanvasTrigger.Cancel,
        ItemOutcome.Abandoned => CanvasTrigger.Abandon,
        ItemOutcome.Expired => CanvasTrigger.Expire,
        _ => CanvasTrigger.Complete,
    };

    private bool Owns(SessionId session, TurnId turn, ItemId item) => session == Session && new CanvasId(turn, item) == Id;
}
