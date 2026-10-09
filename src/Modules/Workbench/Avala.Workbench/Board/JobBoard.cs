using System.Collections.Immutable;
using System.Threading.Channels;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Workbench.Board;

internal sealed class JobBoard
{
    private ImmutableDictionary<JobId, BoardJob> jobs = ImmutableDictionary<JobId, BoardJob>.Empty;
    private ImmutableList<ChannelWriter<bool>> watchers = [];

    public ImmutableDictionary<JobId, BoardJob> Jobs => Volatile.Read(ref jobs);

    public Option<BoardJob> Find(JobId job) =>
        Jobs.TryGetValue(job, out var found) ? Option<BoardJob>.Some(found) : Option<BoardJob>.None;

    public void Publish(ImmutableDictionary<JobId, BoardJob> next)
    {
        Volatile.Write(ref jobs, next);

        foreach (var watcher in Volatile.Read(ref watchers))
        {
            watcher.TryWrite(true);
        }
    }

    public IAsyncEnumerable<bool> ChangesAsync(CancellationToken cancellationToken)
    {
        var changes = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
        });

        ImmutableInterlocked.Update(ref watchers, current => current.Add(changes.Writer));
        changes.Writer.TryWrite(true);
        cancellationToken.Register(() =>
        {
            ImmutableInterlocked.Update(ref watchers, current => current.Remove(changes.Writer));
            changes.Writer.TryComplete();
        });

        return changes.Reader.ReadAllAsync(CancellationToken.None);
    }
}
