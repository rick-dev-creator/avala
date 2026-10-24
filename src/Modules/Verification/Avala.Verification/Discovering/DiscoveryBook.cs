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

    public string Declaration { get; init; } = string.Empty;

    public bool Settled => State != DiscoveryState.Running;

    public IReadOnlyList<DeclaredCheck> Checks =>
        Declaration.Length == 0 ? [] : CheckDeclaration.Parse(Declaration).Match(checks => checks, _ => []);

    public ChecksDiscovery Public => new(Job, Repository, State, [.. Checks.Select(check => new CheckDeclared(check.Name, check.CommandLine, check.Timeout))]);
}

internal interface IDiscoveryStore
{
    Task<IReadOnlyList<Discovery>> DiscoveriesAsync(CancellationToken cancellationToken);

    Task KeepAsync(Discovery discovery, CancellationToken cancellationToken);
}

internal sealed class DiscoveryBook(IDiscoveryStore store, IEventBus bus, TimeProvider clock) : IChecksDiscoveries, IStartupTask
{
    private ImmutableDictionary<JobId, Discovery> discoveries = ImmutableDictionary<JobId, Discovery>.Empty;
    private ImmutableDictionary<JobId, TaskCompletionSource<Discovery>> waiters = ImmutableDictionary<JobId, TaskCompletionSource<Discovery>>.Empty;

    public async Task RunAsync(CancellationToken cancellationToken) =>
        Volatile.Write(ref discoveries, (await store.DiscoveriesAsync(cancellationToken)).ToImmutableDictionary(discovery => discovery.Job));

    public Option<ChecksDiscovery> OfJob(JobId job) => Of(job).Map(discovery => discovery.Public);

    public Option<Discovery> Of(JobId job) => Volatile.Read(ref discoveries).TryGetValue(job, out var found) ? found : Option<Discovery>.None;

    public Option<Discovery> OfScout(JobId scout) =>
        Volatile.Read(ref discoveries).Values.FirstOrDefault(discovery => !discovery.Settled && discovery.Scout == Option<JobId>.Some(scout)).ToOption();

    public bool IsScout(JobId job) => Volatile.Read(ref discoveries).Values.Any(discovery => discovery.Scout == Option<JobId>.Some(job));

    public IReadOnlyList<Discovery> Running() => [.. Volatile.Read(ref discoveries).Values.Where(discovery => !discovery.Settled)];

    public Option<Discovery> LatestFound(string repository) =>
        Volatile.Read(ref discoveries).Values
            .Where(discovery => discovery.State == DiscoveryState.Found && discovery.Repository == repository)
            .MaxBy(discovery => discovery.At)
            .ToOption();

    public DateTimeOffset Now => clock.GetUtcNow();

    public async Task KeepAsync(Discovery changed, CancellationToken cancellationToken)
    {
        var discovery = changed with { At = clock.GetUtcNow() };
        await store.KeepAsync(discovery, cancellationToken);
        ImmutableInterlocked.Update(ref discoveries, kept => kept.SetItem(discovery.Job, discovery));

        if (discovery.Settled && ImmutableInterlocked.TryRemove(ref waiters, discovery.Job, out var waiter))
        {
            waiter.TrySetResult(discovery);
        }

        await bus.PublishAsync(new ChecksDiscoveryChanged(discovery.Public), cancellationToken);
    }

    public async Task<Option<Discovery>> SettledAsync(JobId job, CancellationToken cancellationToken)
    {
        var waiter = ImmutableInterlocked.GetOrAdd(ref waiters, job, _ => new TaskCompletionSource<Discovery>(TaskCreationOptions.RunContinuationsAsynchronously));
        var found = Of(job);

        if (found.Match(discovery => !discovery.Settled, () => false))
        {
            return await waiter.Task.WaitAsync(cancellationToken);
        }

        _ = ImmutableInterlocked.TryRemove(ref waiters, job, out _);

        return found;
    }
}
