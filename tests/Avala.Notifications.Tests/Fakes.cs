using System.Collections.Concurrent;
using Avala.Jobs.Contracts;
using Avala.Notifications.Composing;
using Avala.Notifications.Contracts;
using Avala.Notifications.Delivering;
using Avala.Notifications.Dispatching;
using Avala.Notifications.Records;
using Avala.Sdk;
using Avala.Sdk.Diagnostics;
using Avala.Sdk.Presence;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Notifications.Tests;

internal sealed class FakePresence(FakeTimeProvider clock) : IPresence
{
    public DateTimeOffset LastSeen { get; set; } = clock.GetUtcNow() - TimeSpan.FromHours(1);

    public void SeenNow() => LastSeen = clock.GetUtcNow();
}

internal sealed class FakeRedaction : IRedaction
{
    public string Apply(string text) => text.Replace("hunter2", "[redacted]", StringComparison.Ordinal);
}

internal sealed class FakeFile : INotificationFile
{
    public NotificationFile Current { get; set; } = new("/data/notifications.json", NotificationFileStatus.Absent);

    public string Path => Current.Path;

    public FakeFile With(NotificationSettings settings)
    {
        Current = new NotificationFile(Path, NotificationFileStatus.Applied) { Settings = settings };

        return this;
    }

    public ValueTask<NotificationFile> ReadAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Current);

    public ValueTask<Result<NotificationFile, NotificationError>> WriteAsync(NotificationSettings settings, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Result<NotificationFile, NotificationError>.Success(With(settings).Current));
}

internal sealed class FakeChannel : INotificationChannel
{
    private readonly ConcurrentQueue<NotificationError> failures = new();

    public ConcurrentQueue<OutgoingMessage> Sent { get; } = new();

    public ChannelKind Kind => ChannelKind.Ntfy;

    public FakeChannel Failing(params NotificationError[] errors)
    {
        foreach (var error in errors)
        {
            failures.Enqueue(error);
        }

        return this;
    }

    public Task<Result<OutgoingMessage, NotificationError>> SendAsync(OutgoingMessage message, NotificationSettings settings, CancellationToken cancellationToken)
    {
        Sent.Enqueue(message);

        return Task.FromResult(failures.TryDequeue(out var error) ? Result<OutgoingMessage, NotificationError>.Failure(error) : message);
    }
}

internal sealed class FakeSecrets : ISecrets
{
    public Dictionary<string, string> Values { get; } = [];

    public Option<string> Of(string variable) => Values.TryGetValue(variable, out var value) ? value : Option<string>.None;
}

internal sealed class MemoryStore : INotificationStore
{
    public List<SentSubject> Kept { get; } = [];

    public List<NotificationRecord> Records { get; } = [];

    public Task<IReadOnlyList<SentSubject>> SentAsync(DateTimeOffset since, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SentSubject>>([.. Kept.Where(sent => sent.At >= since)]);

    public Task KeepSentAsync(IReadOnlyList<SentSubject> sent, CancellationToken cancellationToken)
    {
        Kept.AddRange(sent);

        return Task.CompletedTask;
    }

    public Task ForgetAsync(string handle, CancellationToken cancellationToken)
    {
        Kept.RemoveAll(sent => sent.Handle == handle);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<NotificationRecord>> RecordsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<NotificationRecord>>([.. Records]);

    public Task AddRecordAsync(NotificationRecord record, CancellationToken cancellationToken)
    {
        Records.Add(record);

        return Task.CompletedTask;
    }
}

internal sealed class FakeCatalog : IJobCatalog
{
    public Dictionary<JobId, JobSummary> Jobs { get; } = [];

    public FakeCatalog With(JobId job, Option<JobId> parent = default)
    {
        Jobs[job] = new JobSummary(job, Samples.Repository, Samples.Instruction, Samples.Monday, JobStatus.Running, default, default, default) { Parent = parent };

        return this;
    }

    public ValueTask<IReadOnlyList<JobSummary>> ListAsync(CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<JobSummary>>([.. Jobs.Values]);

    public ValueTask<Option<JobHistory>> HistoryAsync(JobId job, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Jobs.TryGetValue(job, out var summary) ? Option<JobHistory>.Some(new JobHistory(summary, [], [])) : Option<JobHistory>.None);

    public ValueTask<IReadOnlyList<JobSummary>> ChildrenAsync(JobId parent, CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<JobSummary>>([]);

    public ValueTask<Option<JobTree>> TreeAsync(JobId root, CancellationToken cancellationToken) => ValueTask.FromResult(Option<JobTree>.None);
}
