using Avala.Fleet.Facts;
using Avala.Sdk;
using Microsoft.Extensions.Logging;

namespace Avala.Fleet.Recording;

internal sealed partial class Outbox(IFactStore store, ILogger<Outbox> logger) : IAsyncDisposable
{
    private readonly SerialExecutor executor = new();

    private Action appended = () => { };

    private long unansweredSince;

    private int untrusted;

    public ChangeSignal Changes { get; } = new();

    public Option<DateTimeOffset> UnansweredSince =>
        Interlocked.Read(ref unansweredSince) is var ticks and not 0 ? new DateTimeOffset(ticks, TimeSpan.Zero) : Option<DateTimeOffset>.None;

    public Task<T> RunAsync<T>(Func<IFactStore, CancellationToken, Task<T>> work, CancellationToken cancellationToken) =>
        executor.RunAsync(token => work(store, token), cancellationToken);

    public void WhenAppended(Action nudge) => Volatile.Write(ref appended, nudge);

    public async Task AppendAsync(string scope, Fact fact, CancellationToken cancellationToken)
    {
        await executor.RunAsync(token => store.AppendAsync(scope, fact, token), cancellationToken);
        Nudge();
        Changes.Raise();
    }

    public void Nudge() => Volatile.Read(ref appended)();

    public bool Untrusted => Volatile.Read(ref untrusted) == 1;

    public void Unanswered(DateTimeOffset at, bool certificateUntrusted)
    {
        var cause = certificateUntrusted ? 1 : 0;

        if (Interlocked.Exchange(ref untrusted, cause) != cause)
        {
            Interlocked.Exchange(ref unansweredSince, at.UtcTicks);
            Changes.Raise();
        }
        else if (Interlocked.CompareExchange(ref unansweredSince, at.UtcTicks, 0) == 0)
        {
            Changes.Raise();
        }
    }

    public void Answered()
    {
        Volatile.Write(ref untrusted, 0);

        if (Interlocked.Exchange(ref unansweredSince, 0) != 0)
        {
            Changes.Raise();
        }
    }

    public void Rejected(string kind, string reason) => LogRejected(kind, reason);

    public void RoundFailed(Exception failure) => LogRoundFailed(failure.GetType().Name, failure);

    public ValueTask DisposeAsync() => executor.DisposeAsync();

    [LoggerMessage(Level = LogLevel.Warning, Message = "The fleet server rejected a {Kind} event: {Reason}")]
    private partial void LogRejected(string kind, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "A delivery round to the fleet server failed with {Failure}")]
    private partial void LogRoundFailed(string failure, Exception exception);
}
