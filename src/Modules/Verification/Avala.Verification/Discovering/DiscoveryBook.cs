using System.Collections.Immutable;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Verification.Checks;
using Avala.Verification.Contracts;

namespace Avala.Verification.Discovering;

internal sealed record Discovery(JobId Job, string Repository, DiscoveryState State, DateTimeOffset At)
{
    public Option<JobId> Scout { get; init; }

    public Option<JobId> Follows { get; init; }

    public string Declaration { get; init; } = string.Empty;

    public string Proposal { get; init; } = string.Empty;

    public bool Reads { get; init; }

    public string Ran { get; init; } = string.Empty;

    public bool Final { get; init; }

    public bool Settled => State is DiscoveryState.Found or DiscoveryState.NoneFound;

    public IReadOnlyList<DeclaredCheck> Checks =>
        Declaration.Length == 0 ? [] : CheckDeclaration.Parse(Declaration).Match(checks => checks, _ => []);

    public ChecksDiscovery Public => new(Job, Repository, State, [.. Checks.Select(check => new CheckDeclared(check.Name, check.CommandLine, check.Timeout))]) { Reads = Reads };
}

internal interface IDiscoveryStore
{
    Task<IReadOnlyList<Discovery>> DiscoveriesAsync(CancellationToken cancellationToken);

    Task KeepAsync(Discovery discovery, CancellationToken cancellationToken);
}

internal sealed class DiscoveryBook(IDiscoveryStore store, IEventBus bus, TimeProvider clock) : IChecksDiscoveries, IStartupTask
{
    private ImmutableDictionary<JobId, Discovery> discoveries = ImmutableDictionary<JobId, Discovery>.Empty;

    public DateTimeOffset Now => clock.GetUtcNow();

    public async Task RunAsync(CancellationToken cancellationToken) =>
        Volatile.Write(ref discoveries, (await store.DiscoveriesAsync(cancellationToken)).ToImmutableDictionary(discovery => discovery.Job));

    public Option<ChecksDiscovery> OfJob(JobId job) => Of(job).Map(discovery => Followed(discovery).Match(owner => owner.Public with { Job = job }, () => discovery.Public));

    public Option<Discovery> Followed(Discovery discovery) =>
        discovery.Settled ? Option<Discovery>.None : discovery.Follows.Bind(Of).Bind(owner => owner.State == DiscoveryState.Proposed ? owner : Option<Discovery>.None);

    public async Task AnnounceAsync(JobId job, CancellationToken cancellationToken) =>
        await OfJob(job).Match(found => bus.PublishAsync(new ChecksDiscoveryChanged(found), cancellationToken).AsTask(), () => Task.CompletedTask);

    public Option<Discovery> Of(JobId job) => Volatile.Read(ref discoveries).TryGetValue(job, out var found) ? found : Option<Discovery>.None;

    public Option<Discovery> OfScout(JobId scout) =>
        Volatile.Read(ref discoveries).Values.FirstOrDefault(discovery => !discovery.Settled && discovery.Scout == Option<JobId>.Some(scout)).ToOption();

    public bool IsScout(JobId job) => Volatile.Read(ref discoveries).Values.Any(discovery => discovery.Scout == Option<JobId>.Some(job));

    public IReadOnlyList<Discovery> Running() => [.. Volatile.Read(ref discoveries).Values.Where(discovery => !discovery.Settled)];

    public IReadOnlyList<Discovery> Followers(JobId owner) =>
        [.. Volatile.Read(ref discoveries).Values
            .Where(discovery => !discovery.Settled && discovery.Follows == Option<JobId>.Some(owner))
            .OrderBy(discovery => discovery.At)
            .ThenBy(discovery => discovery.Job.Value)];

    public Option<Discovery> Scouting(string repository) =>
        Volatile.Read(ref discoveries).Values.FirstOrDefault(discovery => !discovery.Settled && discovery.Scout.IsSome && discovery.Repository == repository).ToOption();

    public Option<Discovery> LatestDecided(string repository) =>
        Volatile.Read(ref discoveries).Values
            .Where(discovery => discovery.Settled && discovery.Final && discovery.Repository == repository)
            .MaxBy(discovery => discovery.At)
            .ToOption();

    public async Task KeepAsync(Discovery changed, CancellationToken cancellationToken)
    {
        var discovery = changed with { At = clock.GetUtcNow() };
        await store.KeepAsync(discovery, cancellationToken);
        ImmutableInterlocked.Update(ref discoveries, kept => kept.SetItem(discovery.Job, discovery));
        await AnnounceAsync(discovery.Job, cancellationToken);
    }
}
