using System.Collections.Immutable;
using System.Threading.Channels;
using Avala.Documentation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Projects.Contracts;
using Avala.Sdk;
using Avala.Verification.Contracts;

namespace Avala.Workbench.Board;

internal sealed class JobBoard
{
    private ImmutableDictionary<JobId, BoardJob> jobs = ImmutableDictionary<JobId, BoardJob>.Empty;
    private ImmutableDictionary<JobId, BoardContest> contests = ImmutableDictionary<JobId, BoardContest>.Empty;
    private ImmutableDictionary<JobId, BoardRemote> remotes = ImmutableDictionary<JobId, BoardRemote>.Empty;
    private ProjectMap projects = ProjectMap.Empty;
    private ImmutableHashSet<JobId> drafted = [];
    private ImmutableDictionary<JobId, IReadOnlyList<JobCheckpoint>> checkpoints = ImmutableDictionary<JobId, IReadOnlyList<JobCheckpoint>>.Empty;
    private ImmutableDictionary<JobId, JobDocumentation> docs = ImmutableDictionary<JobId, JobDocumentation>.Empty;
    private ImmutableDictionary<JobId, ChecksDiscovery> discoveries = ImmutableDictionary<JobId, ChecksDiscovery>.Empty;
    private ImmutableList<ChannelWriter<bool>> watchers = [];

    public ImmutableDictionary<JobId, BoardJob> Jobs => Volatile.Read(ref jobs);

    public ImmutableDictionary<JobId, BoardContest> Contests => Volatile.Read(ref contests);

    public ImmutableDictionary<JobId, BoardRemote> Remotes => Volatile.Read(ref remotes);

    public ProjectMap Projects => Volatile.Read(ref projects);

    public ImmutableHashSet<JobId> Drafted => Volatile.Read(ref drafted);

    public ImmutableDictionary<JobId, IReadOnlyList<JobCheckpoint>> Checkpoints => Volatile.Read(ref checkpoints);

    public ImmutableDictionary<JobId, JobDocumentation> Docs => Volatile.Read(ref docs);

    public ImmutableDictionary<JobId, ChecksDiscovery> Discoveries => Volatile.Read(ref discoveries);

    public BoardRemote RemoteOf(JobId job) => Remotes.GetValueOrDefault(job, BoardRemote.None);

    public Option<BoardJob> Find(JobId job) =>
        Jobs.TryGetValue(job, out var found) ? Option<BoardJob>.Some(found) : Option<BoardJob>.None;

    public Option<BoardContest> ContestOf(JobId job) =>
        Contests.TryGetValue(job, out var found) ? Option<BoardContest>.Some(found) : Option<BoardContest>.None;

    public int Revision(JobId job) =>
        (Jobs.TryGetValue(job, out var found) ? found.Revision : 0) + (Contests.TryGetValue(job, out var contest) ? contest.Revision : 0) + RemoteOf(job).Revision;

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

    public void Publish(ImmutableDictionary<JobId, BoardRemote> next)
    {
        Volatile.Write(ref remotes, next);
        Notify();
    }

    public void Publish(ProjectMap next)
    {
        Volatile.Write(ref projects, next);
        Notify();
    }

    public void Publish(ImmutableHashSet<JobId> next)
    {
        Volatile.Write(ref drafted, next);
        Notify();
    }

    public void Publish(ImmutableDictionary<JobId, IReadOnlyList<JobCheckpoint>> next)
    {
        Volatile.Write(ref checkpoints, next);
        Notify();
    }

    public void Publish(ImmutableDictionary<JobId, JobDocumentation> next)
    {
        Volatile.Write(ref docs, next);
        Notify();
    }

    public void Publish(ImmutableDictionary<JobId, ChecksDiscovery> next)
    {
        Volatile.Write(ref discoveries, next);
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
