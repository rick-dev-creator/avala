using System.Collections.Immutable;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;

namespace Avala.Agents.Sessions;

internal sealed class LiveSession : IAsyncDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly Task pump;
    private ImmutableDictionary<ItemId, AgentForm> forms = ImmutableDictionary<ItemId, AgentForm>.Empty;

    public LiveSession(IAgentSession session, AgentCapabilities capabilities, Func<LiveSession, CancellationToken, Task> pump)
    {
        Session = session;
        Capabilities = capabilities;
        this.pump = pump(this, lifetime.Token);
    }

    public IAgentSession Session { get; }

    public AgentCapabilities Capabilities { get; }

    public bool Ended => pump.IsCompleted;

    public Option<AgentForm> OpenForm(ItemId item) => Volatile.Read(ref forms).GetValueOrDefault(item).ToOption();

    public void Track(IAgentEvent accepted) =>
        Volatile.Write(ref forms, accepted switch
        {
            FormRequested requested => forms.SetItem(requested.Item, requested.Form),
            FormAnswered answered => forms.Remove(answered.Item),
            ItemCompleted completed => forms.Remove(completed.Item),
            TurnCompleted => forms.Clear(),
            _ => forms,
        });

    public async ValueTask DisposeAsync()
    {
        await lifetime.CancelAsync();
        await Session.DisposeAsync();
        await pump;
        lifetime.Dispose();
    }
}
