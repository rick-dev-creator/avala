using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;
using Avala.Sdk.Domain;
using Stateless;

namespace Avala.Agents.Domain;

internal sealed class Turn : IAggregateRoot<TurnId>
{
    private readonly Dictionary<ItemId, DateTimeOffset> openItems = [];
    private readonly HashSet<ItemId> completedItems = [];
    private readonly StateMachine<TurnState, TurnTrigger> machine;

    private Turn(SessionId session, TurnId id)
    {
        Session = session;
        Id = id;
        machine = TurnLifecycle.Create(() => State, state => State = state);
    }

    public TurnId Id { get; }

    public SessionId Session { get; }

    public TurnState State { get; private set; } = TurnState.Working;

    public Option<ItemId> PendingPermission { get; private set; }

    public IReadOnlyCollection<ItemId> OpenItems => openItems.Keys;

    public static Result<Turn, TurnError> Begin(TurnStarted started) => new Turn(started.Session, started.Turn);

    public Result<TurnProgress, TurnError> Apply(IAgentEvent agentEvent, DateTimeOffset at)
    {
        if (agentEvent.Session != Session || agentEvent.Turn != Id)
        {
            return TurnError.ForeignEvent;
        }

        if (!machine.IsInState(TurnState.Live))
        {
            return TurnError.TurnEnded;
        }

        return agentEvent switch
        {
            TurnStarted => TurnError.UnexpectedTurnStart,
            ItemStarted started => Open(started, started.Item, at),
            CanvasStarted started => Open(started, started.Item, at),
            ItemProgressed progressed => Touch(progressed, progressed.Item, at),
            ItemCompleted completed => Close(completed),
            PermissionRequested requested => RequestPermission(requested, at),
            PermissionResolved resolved => ResolvePermission(resolved, at),
            TurnCompleted completed => End(completed),
            _ => new TurnProgress([agentEvent]),
        };
    }

    public Result<TurnProgress, TurnError> Expire(DateTimeOffset now, TimeSpan patience)
    {
        if (!machine.IsInState(TurnState.Live))
        {
            return TurnError.TurnEnded;
        }

        var stale = openItems
            .Where(item => PendingPermission != Option<ItemId>.Some(item.Key) && now - item.Value >= patience)
            .OrderBy(item => item.Value)
            .Select(item => item.Key)
            .ToList();

        return new TurnProgress([.. stale.Select(item => Conclude(item, ItemOutcome.Expired))]);
    }

    private Result<TurnProgress, TurnError> Open(IAgentEvent started, ItemId item, DateTimeOffset at)
    {
        if (openItems.ContainsKey(item) || completedItems.Contains(item))
        {
            return TurnError.ItemAlreadyStarted;
        }

        openItems[item] = at;

        return new TurnProgress([started]);
    }

    private Result<TurnProgress, TurnError> Touch(IAgentEvent agentEvent, ItemId item, DateTimeOffset at)
    {
        if (completedItems.Contains(item))
        {
            return TurnError.ItemAlreadyCompleted;
        }

        if (!openItems.ContainsKey(item))
        {
            return TurnError.UnknownItem;
        }

        openItems[item] = at;

        return new TurnProgress([agentEvent]);
    }

    private Result<TurnProgress, TurnError> Close(ItemCompleted completed)
    {
        if (completedItems.Contains(completed.Item))
        {
            return TurnError.ItemAlreadyCompleted;
        }

        if (!openItems.Remove(completed.Item))
        {
            return TurnError.UnknownItem;
        }

        completedItems.Add(completed.Item);

        return new TurnProgress([completed]);
    }

    private Result<TurnProgress, TurnError> RequestPermission(PermissionRequested requested, DateTimeOffset at) =>
        Touch(requested, requested.Item, at).Bind(progress =>
            machine.TryFire(TurnTrigger.RequestPermission, TurnError.PermissionAlreadyPending).Map(_ =>
            {
                PendingPermission = requested.Item;

                return progress;
            }));

    private Result<TurnProgress, TurnError> ResolvePermission(PermissionResolved resolved, DateTimeOffset at)
    {
        if (PendingPermission != Option<ItemId>.Some(resolved.Item))
        {
            return TurnError.NoPendingPermission;
        }

        return machine.TryFire(TurnTrigger.ResolvePermission, TurnError.NoPendingPermission).Bind(_ =>
        {
            PendingPermission = Option<ItemId>.None;

            return Touch(resolved, resolved.Item, at);
        });
    }

    private Result<TurnProgress, TurnError> End(TurnCompleted completed)
    {
        var trigger = completed.Outcome switch
        {
            TurnOutcome.Interrupted => TurnTrigger.Interrupt,
            TurnOutcome.Failed => TurnTrigger.Fail,
            _ => TurnTrigger.Finish,
        };

        return machine.TryFire(trigger, TurnError.TurnEnded).Map(_ =>
        {
            var abandoned = openItems.OrderBy(item => item.Value).Select(item => item.Key).ToList();
            PendingPermission = Option<ItemId>.None;

            return new TurnProgress([.. abandoned.Select(item => Conclude(item, ItemOutcome.Abandoned)), completed]);
        });
    }

    private ItemCompleted Conclude(ItemId item, ItemOutcome outcome)
    {
        openItems.Remove(item);
        completedItems.Add(item);

        return new ItemCompleted(Session, Id, item, outcome);
    }
}
