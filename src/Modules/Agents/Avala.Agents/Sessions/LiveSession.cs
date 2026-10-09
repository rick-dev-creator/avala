using Avala.Agents.Contracts.Sessions;

namespace Avala.Agents.Sessions;

internal sealed class LiveSession : IAsyncDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly Task pump;

    public LiveSession(IAgentSession session, AgentCapabilities capabilities, Func<LiveSession, CancellationToken, Task> pump)
    {
        Session = session;
        Capabilities = capabilities;
        this.pump = pump(this, lifetime.Token);
    }

    public IAgentSession Session { get; }

    public AgentCapabilities Capabilities { get; }

    public bool Ended => pump.IsCompleted;

    public async ValueTask DisposeAsync()
    {
        await lifetime.CancelAsync();
        await Session.DisposeAsync();
        await pump;
        lifetime.Dispose();
    }
}
