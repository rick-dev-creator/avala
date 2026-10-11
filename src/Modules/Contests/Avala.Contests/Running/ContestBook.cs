using System.Collections.Immutable;
using Avala.Contests.Contesting;
using Avala.Contests.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Contests.Running;

internal interface IContestStore
{
    Task<IReadOnlyList<ContestRecord>> ContestsAsync(CancellationToken cancellationToken);

    Task KeepAsync(ContestRecord contest, CancellationToken cancellationToken);
}

internal interface IBestOfFile
{
    Task<Result<BestOf, ContestRejection>> OfRepositoryAsync(string repository, CancellationToken cancellationToken);
}

internal sealed class ContestBook(IContestStore store, IEventBus bus) : IStartupTask, IAsyncDisposable
{
    private readonly SerialExecutor owner = new();
    private readonly Dictionary<ContestId, Contest> contests = [];
    private ImmutableDictionary<ContestId, ContestRecord> records = ImmutableDictionary<ContestId, ContestRecord>.Empty;
    private ImmutableDictionary<JobId, ContestId> candidates = ImmutableDictionary<JobId, ContestId>.Empty;

    public Option<ContestRecord> Of(ContestId contest) =>
        Volatile.Read(ref records).TryGetValue(contest, out var found) ? found : Option<ContestRecord>.None;

    public Option<ContestRecord> OfJob(JobId job) =>
        Volatile.Read(ref candidates).TryGetValue(job, out var contest) ? Of(contest) : Option<ContestRecord>.None;

    public IReadOnlyList<ContestRecord> All() => [.. Volatile.Read(ref records).Values.OrderBy(contest => contest.Started)];

    public Task RunAsync(CancellationToken cancellationToken) =>
        owner.RunAsync(
            async token =>
            {
                foreach (var restored in (await store.ContestsAsync(token)).Select(Contest.Restore))
                {
                    if (restored.TryGetValue(out var contest, out _))
                    {
                        Remember(contest);
                    }
                }

                await bus.PublishAsync(new ContestsRestored(All()), token);
            },
            cancellationToken);

    public Task WorkAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken) => owner.RunAsync(work, cancellationToken);

    public Task<T> WorkAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken) => owner.RunAsync(work, cancellationToken);

    public Option<Contest> Find(ContestId contest) => contests.TryGetValue(contest, out var found) ? found : Option<Contest>.None;

    public Option<Contest> FindOf(JobId job) =>
        Volatile.Read(ref candidates).TryGetValue(job, out var contest) ? Find(contest) : Option<Contest>.None;

    public IReadOnlyList<Contest> Open() =>
        [.. contests.Values.Where(contest => contest.Status is not (ContestStatus.Closed or ContestStatus.Cancelled)).OrderBy(contest => contest.Started)];

    public async Task KeepAsync<TEvent>(Contest contest, Func<ContestRecord, TEvent> announced, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent
    {
        var record = contest.Record;
        await store.KeepAsync(record, cancellationToken);
        Remember(contest);
        await bus.PublishAsync(announced(record), cancellationToken);
    }

    public ValueTask DisposeAsync() => owner.DisposeAsync();

    private void Remember(Contest contest)
    {
        contests[contest.Id] = contest;
        ImmutableInterlocked.Update(ref records, known => known.SetItem(contest.Id, contest.Record));
        ImmutableInterlocked.Update(ref candidates, known => known.SetItems(contest.Candidates.Select(candidate => KeyValuePair.Create(candidate.Job, contest.Id))));
    }
}
