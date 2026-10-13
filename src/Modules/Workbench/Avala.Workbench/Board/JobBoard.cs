using System.Collections.Immutable;
using System.Threading.Channels;
using Avala.Jobs.Contracts;
using Avala.Projects.Contracts;
using Avala.Sdk;

namespace Avala.Workbench.Board;

internal sealed class JobBoard
{
    private ImmutableDictionary<JobId, BoardJob> jobs = ImmutableDictionary<JobId, BoardJob>.Empty;
    private ImmutableDictionary<JobId, BoardContest> contests = ImmutableDictionary<JobId, BoardContest>.Empty;
    private ProjectMap projects = ProjectMap.Empty;
    private ImmutableList<ChannelWriter<bool>> watchers = [];

    public ImmutableDictionary<JobId, BoardJob> Jobs => Volatile.Read(ref jobs);

    public ImmutableDictionary<JobId, BoardContest> Contests => Volatile.Read(ref contests);

    public ProjectMap Projects => Volatile.Read(ref projects);

    public Option<BoardJob> Find(JobId job) =>
        Jobs.TryGetValue(job, out var found) ? Option<BoardJob>.Some(found) : Option<BoardJob>.None;

    public Option<BoardContest> ContestOf(JobId job) =>
        Contests.TryGetValue(job, out var found) ? Option<BoardContest>.Some(found) : Option<BoardContest>.None;

    public int Revision(JobId job) =>
        (Jobs.TryGetValue(job, out var found) ? found.Revision : 0) + (Contests.TryGetValue(job, out var contest) ? contest.Revision : 0);

    public void Publish(ImmutableDictionary<JobId, BoardJob> next)
    {
        Volatile.Write(ref jobs, next);
        Notify();
    }

    public void Publish(ImmutableDictionary<JobId, BoardContest> next)
    {
        Volatile.Write(ref contests, next);
        Notify();
    }

    public void Publish(ProjectMap next)
    {
        Volatile.Write(ref projects, next);
        Notify();
    }

    private void Notify()
    {
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
