using System.Collections.Immutable;
using Avala.Autopilot.Contracts;
using Avala.Autopilot.Loops;
using Avala.Sdk;
using Avala.Sdk.Events;
using Microsoft.Extensions.Logging;

namespace Avala.Autopilot.Looping;

internal sealed class LoopBook
{
    private ImmutableDictionary<LoopId, LoopRecord> loops = ImmutableDictionary<LoopId, LoopRecord>.Empty;

    public void Keep(LoopRecord record) => ImmutableInterlocked.AddOrUpdate(ref loops, record.Id, record, (_, _) => record);

    public IReadOnlyList<LoopState> States() => [.. Volatile.Read(ref loops).Values.OrderBy(record => record.Started).Select(record => record.State)];

    public Option<LoopDigest> DigestOf(LoopId loop) =>
        Volatile.Read(ref loops).TryGetValue(loop, out var record) ? record.Digest : Option<LoopDigest>.None;
}

internal sealed partial class LoopJournal(IEventBus bus, LoopBook book, ILogger<LoopJournal> logger)
{
    public LoopBook Book => book;

    public void Keep(LoopRecord record) => book.Keep(record);

    public ValueTask PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent =>
        bus.PublishAsync(integrationEvent, cancellationToken);

    public void Failed(LoopId loop, Exception exception) => LogFailed(loop.Value, exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Work on autopilot loop {Loop} failed")]
    private partial void LogFailed(Guid loop, Exception exception);
}
