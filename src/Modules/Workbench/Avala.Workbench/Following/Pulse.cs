using System.Collections.Immutable;
using System.Threading.Channels;
using Avala.Budgets.Contracts;
using Avala.Delegation.Contracts;
using Avala.Observability.Contracts;
using Avala.Resources.Contracts;
using Avala.Sdk.Events;
using Avala.Supervision.Contracts;
using Avala.Workbench.Board;

namespace Avala.Workbench.Following;

internal sealed class Pulse(JobBoard board)
    : IHandle<UsageRecorded>,
        IHandle<ResourcesSampled>,
        IHandle<OrphansFound>,
        IHandle<OrphansReaped>,
        IHandle<WorktreeReclaimed>,
        IHandle<WorktreesReconciled>,
        IHandle<ChildDelegated>,
        IHandle<DelegationRefused>,
        IHandle<ChildReported>,
        IHandle<BudgetCarved>,
        IHandle<BudgetIntervened>,
        IHandle<SupervisorIntervened>
{
    private ImmutableList<ChannelWriter<bool>> watchers = [];

    public ValueTask HandleAsync(UsageRecorded integrationEvent, CancellationToken cancellationToken) => BeatAsync();

    public ValueTask HandleAsync(ResourcesSampled integrationEvent, CancellationToken cancellationToken) => BeatAsync();

    public ValueTask HandleAsync(OrphansFound integrationEvent, CancellationToken cancellationToken) => BeatAsync();

    public ValueTask HandleAsync(OrphansReaped integrationEvent, CancellationToken cancellationToken) => BeatAsync();

    public ValueTask HandleAsync(WorktreeReclaimed integrationEvent, CancellationToken cancellationToken) => BeatAsync();

    public ValueTask HandleAsync(WorktreesReconciled integrationEvent, CancellationToken cancellationToken) => BeatAsync();

    public ValueTask HandleAsync(ChildDelegated integrationEvent, CancellationToken cancellationToken) => BeatAsync();

    public ValueTask HandleAsync(DelegationRefused integrationEvent, CancellationToken cancellationToken) => BeatAsync();

    public ValueTask HandleAsync(ChildReported integrationEvent, CancellationToken cancellationToken) => BeatAsync();

    public ValueTask HandleAsync(BudgetCarved integrationEvent, CancellationToken cancellationToken) => BeatAsync();

    public ValueTask HandleAsync(BudgetIntervened integrationEvent, CancellationToken cancellationToken) => BeatAsync();

    public ValueTask HandleAsync(SupervisorIntervened integrationEvent, CancellationToken cancellationToken) => BeatAsync();

    public void Beat()
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
        _ = ForwardAsync(board.ChangesAsync(cancellationToken), changes.Writer);
        cancellationToken.Register(() =>
        {
            ImmutableInterlocked.Update(ref watchers, current => current.Remove(changes.Writer));
            changes.Writer.TryComplete();
        });

        return changes.Reader.ReadAllAsync(CancellationToken.None);
    }

    private ValueTask BeatAsync()
    {
        Beat();

        return ValueTask.CompletedTask;
    }

    private static async Task ForwardAsync(IAsyncEnumerable<bool> source, ChannelWriter<bool> target)
    {
        await foreach (var change in source)
        {
            target.TryWrite(change);
        }
    }
}
