using System.Collections.Immutable;
using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;

namespace Avala.Agents.Sessions;

internal sealed class LiveSession : IAsyncDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly Task pump;
    private ImmutableDictionary<ItemId, AgentForm> forms = ImmutableDictionary<ItemId, AgentForm>.Empty;
    private ImmutableHashSet<ItemId> calls = [];
    private bool inTurn;

    public LiveSession(IAgentSession session, CapabilitySet capabilities, Func<LiveSession, CancellationToken, Task> pump)
    {
        Session = session;
        Capabilities = capabilities;
        this.pump = pump(this, lifetime.Token);
    }

    public IAgentSession Session { get; }

    public CapabilitySet Capabilities { get; }

    public bool Ended => pump.IsCompleted;

    public bool ClosesItsTurn { get; private set; }

    public bool InTurn => Volatile.Read(ref inTurn);

    public Option<AgentForm> OpenForm(ItemId item) => Volatile.Read(ref forms).GetValueOrDefault(item).ToOption();

    public bool AwaitsResult(ItemId item) => Volatile.Read(ref calls).Contains(item);

    public IAgentEvent Priced(IAgentEvent reported) =>
        reported is UsageReported usage
            ? usage with { Cost = usage.Cost.Map(cost => cost with { Basis = Capabilities.Get<ReportsCost>().Match(declared => declared.Basis, () => CostBasis.Billed) }) }
            : reported;

    public void Track(IAgentEvent accepted)
    {
        Volatile.Write(ref inTurn, accepted switch
        {
            TurnStarted => true,
            TurnCompleted => false,
            _ => inTurn,
        });
        Volatile.Write(ref forms, accepted switch
        {
            FormRequested requested => forms.SetItem(requested.Item, requested.Form),
            FormAnswered answered => forms.Remove(answered.Item),
            RequestWithdrawn withdrawn => forms.Remove(withdrawn.Item),
            ItemCompleted completed => forms.Remove(completed.Item),
            TurnCompleted => forms.Clear(),
            _ => forms,
        });
        Volatile.Write(ref calls, accepted switch
        {
            ToolCalled called => calls.Add(called.Item),
            ToolReturned returned => calls.Remove(returned.Item),
            ItemCompleted completed => calls.Remove(completed.Item),
            TurnCompleted => calls.Clear(),
            _ => calls,
        });
    }

    public ValueTask StopAsync()
    {
        ClosesItsTurn = true;

        return DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await lifetime.CancelAsync();
        await Session.DisposeAsync();
        await pump;
        lifetime.Dispose();
    }
}
