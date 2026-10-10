using System.Collections.Immutable;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Delegation.Records;

internal sealed record CallRef(SessionId Session, ItemId Item);

internal sealed record QueuedAsking(SessionId Session, ItemId Request, ToolResult Result);

internal sealed class OpenCalls
{
    private ImmutableDictionary<JobId, CallRef> open = ImmutableDictionary<JobId, CallRef>.Empty;
    private ImmutableDictionary<JobId, QueuedAsking> queued = ImmutableDictionary<JobId, QueuedAsking>.Empty;

    public void Queue(JobId child, QueuedAsking asking) => ImmutableInterlocked.AddOrUpdate(ref queued, child, asking, (_, _) => asking);

    public Option<QueuedAsking> TakeQueued(JobId child) => ImmutableInterlocked.TryRemove(ref queued, child, out var asking) ? asking : Option<QueuedAsking>.None;

    private ImmutableHashSet<JobId> expected = [];

    public void Open(JobId child, CallRef call)
    {
        ImmutableInterlocked.Update(ref expected, waiting => waiting.Remove(child));
        ImmutableInterlocked.AddOrUpdate(ref open, child, call, (_, _) => call);
    }

    public void Expect(JobId child) => ImmutableInterlocked.Update(ref expected, waiting => waiting.Add(child));

    public bool Expects(JobId child) => Volatile.Read(ref expected).Contains(child);

    public Option<CallRef> Take(JobId child) => ImmutableInterlocked.TryRemove(ref open, child, out var call) ? call : Option<CallRef>.None;

    public IReadOnlyList<(JobId Child, CallRef Call)> TakeAll(SessionId session) =>
        [
            .. Volatile.Read(ref open)
                .Where(pending => pending.Value.Session == session)
                .Select(pending => pending.Key)
                .SelectMany(child => Take(child).Match<(JobId, CallRef)[]>(call => [(child, call)], () => [])),
        ];
}
