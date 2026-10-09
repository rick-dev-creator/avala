using System.Collections.Concurrent;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Transcripts.Contracts;
using Avala.Transcripts.Keeping;

namespace Avala.Transcripts.Tests.Keeping;

internal sealed class FakeLog : ITranscriptLog
{
    private readonly ConcurrentQueue<(JobId Job, DateTimeOffset At, ITranscriptFact Fact)> kept = new();

    public Dictionary<JobId, int> Earlier { get; } = [];

    public IReadOnlyList<(JobId Job, DateTimeOffset At, ITranscriptFact Fact)> Kept => [.. kept];

    public IReadOnlyList<ITranscriptFact> Facts => [.. kept.Select(entry => entry.Fact)];

    public void Keep(JobId job, DateTimeOffset at, ITranscriptFact fact) => kept.Enqueue((job, at, fact));

    public List<JobId> Released { get; } = [];

    public void Release(JobId job) => Released.Add(job);

    public Task<int> AttemptsAsync(JobId job, CancellationToken cancellationToken) =>
        Task.FromResult(Earlier.GetValueOrDefault(job));

    public Task<IReadOnlyList<KeptFact>> EarlierRunsAsync(JobId job, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<KeptFact>>([]);
}

internal sealed class FakeCatalog : IJobCatalog
{
    public Dictionary<JobId, int> Attempts { get; } = [];

    public ValueTask<IReadOnlyList<JobSummary>> ListAsync(CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<JobSummary>>([]);

    public ValueTask<Option<JobHistory>> HistoryAsync(JobId job, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Attempts.TryGetValue(job, out var count)
            ? Option<JobHistory>.Some(new JobHistory(
                new JobSummary(job, "/repo", "Add GitHub login", DateTimeOffset.UnixEpoch, JobStatus.Running, default, default, default),
                [],
                [.. Enumerable.Range(1, count).Select(number => new AttemptRecord(number, AttemptOrigin.Initial, AttemptOutcome.Running, Option<string>.None, default))]))
            : Option<JobHistory>.None);

    public ValueTask<IReadOnlyList<JobSummary>> ChildrenAsync(JobId parent, CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<JobSummary>>([]);

    public ValueTask<Option<JobTree>> TreeAsync(JobId root, CancellationToken cancellationToken) => ValueTask.FromResult(Option<JobTree>.None);
}
