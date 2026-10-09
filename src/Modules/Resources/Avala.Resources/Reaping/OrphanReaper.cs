using System.Collections.Immutable;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Resources.Contracts;
using Avala.Resources.Sampling;
using Avala.Resources.Tracking;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Sdk.Processes;

namespace Avala.Resources.Reaping;

internal sealed class OrphanReaper(ProcessReadings readings, IResourceSettings settings, IEventBus bus, TimeProvider clock) : IOrphans
{
    private ImmutableList<OrphanReport> audit = [];
    private ImmutableDictionary<ProcessTreeId, OrphanReport> left = ImmutableDictionary<ProcessTreeId, OrphanReport>.Empty;

    public IReadOnlyList<OrphanReport> Audit() => Volatile.Read(ref audit);

    public IReadOnlyList<OrphanReport> OfJob(JobId job) => [.. Audit().Where(report => report.Job == Option<JobId>.Some(job))];

    public Task ReapEndedAsync(ProcessTreeId tree, SessionId session, Option<JobId> job, CancellationToken cancellationToken) =>
        Volatile.Read(ref left).ContainsKey(tree)
            ? Task.CompletedTask
            : readings.Find(tree).Match(open => ReapOpenAsync(open, session, job, cancellationToken), () => Task.CompletedTask);

    private async Task ReapOpenAsync(IProcessTree open, SessionId session, Option<JobId> job, CancellationToken cancellationToken)
    {
        var tree = open.Id;
        var orphans = await readings.ReadAsync(open, cancellationToken);

        if (orphans.Count == 0)
        {
            _ = await readings.CloseAsync(tree, cancellationToken);
            return;
        }

        var found = new OrphanReport(tree, orphans, OrphanDisposal.LeftRunning, [], clock.GetUtcNow()) { Session = session, Job = job };

        if ((await settings.LoadAsync(cancellationToken)).Orphans == OrphanPolicy.Kill)
        {
            found = await KillAsync(found, cancellationToken);
        }
        else
        {
            ImmutableInterlocked.TryAdd(ref left, tree, found);
        }

        Record(found);
        await bus.PublishAsync(new OrphansFound(found), cancellationToken);
    }

    public async ValueTask<Result<IReadOnlyList<OrphanReport>, ResourceError>> ReapAsync(JobId job, CancellationToken cancellationToken)
    {
        var reaped = new List<OrphanReport>();

        foreach (var report in Volatile.Read(ref left).Values.Where(report => report.Job == Option<JobId>.Some(job)))
        {
            if (ImmutableInterlocked.TryRemove(ref left, report.Tree, out _))
            {
                var killed = await KillAsync(report, cancellationToken);
                Record(killed);
                reaped.Add(killed);
                await bus.PublishAsync(new OrphansReaped(killed), cancellationToken);
            }
        }

        return reaped.Count == 0 ? ResourceError.NothingToReap : reaped;
    }

    private async Task<OrphanReport> KillAsync(OrphanReport report, CancellationToken cancellationToken)
    {
        var survivors = await readings.CloseAsync(report.Tree, cancellationToken);

        return report with { Disposal = OrphanDisposal.Killed, Survivors = [.. survivors.Select(survivor => survivor.Id)], At = clock.GetUtcNow() };
    }

    private void Record(OrphanReport report) => ImmutableInterlocked.Update(ref audit, known => known.Add(report));
}
