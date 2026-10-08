using Avala.Agents.Contracts.Sessions;

namespace Avala.Agents.Application;

internal sealed class LiveSession : IAsyncDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly Task pump;

    public LiveSession(IAgentSession session, Func<IAgentSession, CancellationToken, Task> pump)
    {
        Session = session;
        this.pump = pump(session, lifetime.Token);
    }

    public IAgentSession Session { get; }

    public async ValueTask DisposeAsync()
    {
        await lifetime.CancelAsync();
        await Session.DisposeAsync();
        await pump;
        lifetime.Dispose();
    }
}
