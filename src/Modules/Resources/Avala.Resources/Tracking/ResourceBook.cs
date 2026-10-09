using System.Collections.Immutable;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Resources.Contracts;
using Avala.Resources.Leasing;
using Avala.Resources.Usage;
using Avala.Sdk;

namespace Avala.Resources.Tracking;

internal sealed class ResourceBook(IResourceSettings settings, PortLeases leases) : IResources
{
    private Snapshot state = new(Attribution.Empty, Option<ResourceSample>.None);
    private ImmutableList<PortConflict> conflicts = [];

    public Attribution Attribution => Volatile.Read(ref state).Attribution;

    public Option<ResourceSample> Latest => Volatile.Read(ref state).Sample;

    public void Attribute(Func<Attribution, Attribution> change) =>
        ImmutableInterlocked.Update(ref state, known => known with { Attribution = change(known.Attribution) });

    public void Keep(ResourceSample sample) => ImmutableInterlocked.Update(ref state, known => known with { Sample = sample });

    public void Observe(PortConflict conflict) => ImmutableInterlocked.Update(ref conflicts, observed => observed.Add(conflict));

    public ResourceUsage Global() =>
        Latest.Match(sample => Tallies.Tally(sample.Trees, sample.DataFolderBytes), () => Tallies.Nothing);

    public ResourceUsage OfJob(JobId job) =>
        Tally(tree => tree.Job == Option<JobId>.Some(job), folder => folder.Job == Option<JobId>.Some(job));

    public ResourceUsage OfSession(SessionId session)
    {
        var home = Attribution.Of(session).Map(facts => facts.Home);

        return Tally(tree => tree.Session == Option<SessionId>.Some(session), folder => home == Option<string>.Some(folder.Path));
    }

    public IReadOnlyList<ConnectionResources> ByConnection() =>
    [
        .. Attribution.Sessions.Values
            .GroupBy(facts => facts.Connection)
            .OrderBy(group => group.Key.Value, StringComparer.Ordinal)
            .Select(group => new ConnectionResources(group.Key, Tally(
                tree => tree.Connection == Option<ConnectionName>.Some(group.Key),
                folder => group.Any(facts => facts.Home == folder.Path)))),
    ];

    public IReadOnlyList<ProviderResources> ByProvider() =>
    [
        .. Attribution.Sessions.Values
            .GroupBy(facts => facts.Provider)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new ProviderResources(group.Key, Tally(
                tree => tree.Provider == Option<string>.Some(group.Key),
                folder => group.Any(facts => facts.Home == folder.Path)))),
    ];

    public IReadOnlyList<PortLease> Leases() => leases.Current;

    public IReadOnlyList<PortConflict> Conflicts() => Volatile.Read(ref conflicts);

    public ValueTask<ResourceSettings> SettingsAsync(CancellationToken cancellationToken) => settings.LoadAsync(cancellationToken);

    private ResourceUsage Tally(Func<TreeUsage, bool> tree, Func<FolderUsage, bool> folder) =>
        Latest.Match(
            sample => Tallies.Tally(sample.Trees.Where(tree), sample.Worktrees.Where(folder).Sum(used => used.Bytes)),
            () => Tallies.Nothing);

    private sealed record Snapshot(Attribution Attribution, Option<ResourceSample> Sample);
}
