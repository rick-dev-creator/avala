using System.Collections.Immutable;
using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Handoffs.Watching;

internal sealed class Compaction(IAgents agents, IConnections connections, TimeProvider clock) : IHandle<TurnFinished>
{
    public static readonly TimeSpan Patience = TimeSpan.FromMinutes(5);

    private ImmutableDictionary<SessionId, TaskCompletionSource<TurnOutcome>> waiting = ImmutableDictionary<SessionId, TaskCompletionSource<TurnOutcome>>.Empty;

    public ValueTask HandleAsync(TurnFinished integrationEvent, CancellationToken cancellationToken)
    {
        if (ImmutableInterlocked.TryRemove(ref waiting, integrationEvent.Session, out var finished))
        {
            finished.TrySetResult(integrationEvent.Outcome);
        }

        return ValueTask.CompletedTask;
    }

    public async Task<Option<SessionId>> CompactableAsync(JobSituation situation, CancellationToken cancellationToken)
    {
        if (!situation.Situation.Rules.CompactOnResume || situation.History.Sessions is not [.., var last] || !agents.IsOpen(last.Session) || agents.IsWorking(last.Session))
        {
            return Option<SessionId>.None;
        }

        return (await MessageAsync(situation.Connection, cancellationToken)).Map(_ => last.Session);
    }

    public async Task<bool> CompactAsync(JobSituation situation, SessionId session, CancellationToken cancellationToken)
    {
        var message = (await MessageAsync(situation.Connection, cancellationToken)).Match(found => found, () => string.Empty);

        if (message.Length == 0)
        {
            return false;
        }

        var finished = new TaskCompletionSource<TurnOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        ImmutableInterlocked.Update(ref waiting, known => known.SetItem(session, finished));

        if ((await agents.SayAsync(session, new UserTurn(message) { Author = MessageAuthor.Avala }, cancellationToken)).IsFailure)
        {
            ImmutableInterlocked.TryRemove(ref waiting, session, out _);

            return false;
        }

        try
        {
            return await finished.Task.WaitAsync(Patience, clock, cancellationToken) == TurnOutcome.Finished;
        }
        catch (TimeoutException)
        {
            ImmutableInterlocked.TryRemove(ref waiting, session, out _);

            return false;
        }
    }

    private async Task<Option<string>> MessageAsync(ConnectionName connection, CancellationToken cancellationToken) =>
        (await connections.CheckAsync(connection, cancellationToken)).Match(
            found => found.Capabilities.Get<CompactsOnRequest>().Map(compact => compact.Message),
            _ => Option<string>.None);
}
