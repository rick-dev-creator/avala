using System.Collections.Concurrent;
using Avala.Agents.Contracts.Connections;
using Avala.Autopilot.Contracts;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Triggers.Catalog;
using Avala.Triggers.Contracts;
using Avala.Triggers.Firing;
using Avala.Triggers.Receiving;
using Avala.Triggers.Records;
using Avala.Triggers.Scheduling;
using Avala.Triggers.TriggerFiles;

namespace Avala.Triggers.Tests;

internal sealed class FakeTriggerFiles : ITriggerFiles
{
    public string Machine { get; set; } = "{}";

    public ValueTask<FileReading> MachineAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(TriggerFileParser.ParseMachine(Machine).Match(
            parsed => new FileReading(new TriggerFile("triggers.json", TriggerFileStatus.Applied) { Triggers = parsed.Triggers.Count }, parsed.Triggers, parsed.Repositories),
            error => new FileReading(new TriggerFile("triggers.json", TriggerFileStatus.Rejected) { Error = error }, [], [])));

    public ValueTask<FileReading> RepositoryAsync(string repository, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new FileReading(new TriggerFile(repository, TriggerFileStatus.Absent), [], []));
}

internal sealed class MemoryTriggerStore : IScheduleStore, IRunStore
{
    private readonly ConcurrentDictionary<string, ScheduleState> schedules = new();
    private readonly ConcurrentDictionary<Guid, TriggerRun> runs = new();
    private readonly ConcurrentQueue<WebhookDelivery> deliveries = new();

    public Task<IReadOnlyList<ScheduleState>> SchedulesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ScheduleState>>([.. schedules.Values]);

    public Task KeepAsync(ScheduleState state, CancellationToken cancellationToken)
    {
        schedules[state.Trigger] = state;

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<TriggerRun>> RunsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TriggerRun>>([.. runs.Values.OrderBy(run => run.Id)]);

    public Task KeepRunAsync(TriggerRun run, CancellationToken cancellationToken)
    {
        runs[run.Id] = run;

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<WebhookDelivery>> DeliveriesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<WebhookDelivery>>([.. deliveries]);

    public Task AddDeliveryAsync(WebhookDelivery delivery, CancellationToken cancellationToken)
    {
        deliveries.Enqueue(delivery);

        return Task.CompletedTask;
    }
}

internal sealed class FakeJobs : IJobs, IJobCatalog
{
    private readonly ConcurrentDictionary<JobId, JobSummary> jobs = new();
    private readonly ConcurrentQueue<JobRequest> requests = new();

    public IReadOnlyList<JobRequest> Requests => [.. requests];

    public Option<JobRejection> Refusal { get; set; }

    public void Settle(JobId job, JobStatus status) => jobs[job] = jobs[job] with { Status = status };

    public ValueTask<Result<JobId, JobRejection>> SubmitAsync(JobRequest request, CancellationToken cancellationToken)
    {
        requests.Enqueue(request);

        return ValueTask.FromResult(Refusal.Match(
            Result<JobId, JobRejection>.Failure,
            () =>
            {
                var job = JobId.New();
                jobs[job] = new JobSummary(job, request.RepositoryPath, request.Instruction, DateTimeOffset.UnixEpoch, JobStatus.Preparing, request.Connection, request.Autonomy, Option<Workspaces.Contracts.WorkspaceId>.None);

                return Result<JobId, JobRejection>.Success(job);
            }));
    }

    public ValueTask<IReadOnlyList<JobSummary>> ListAsync(CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<JobSummary>>([.. jobs.Values]);

    public ValueTask<Option<JobHistory>> HistoryAsync(JobId job, CancellationToken cancellationToken) => ValueTask.FromResult(Option<JobHistory>.None);

    public ValueTask<IReadOnlyList<JobSummary>> ChildrenAsync(JobId parent, CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<JobSummary>>([]);

    public ValueTask<Option<JobTree>> TreeAsync(JobId root, CancellationToken cancellationToken) => ValueTask.FromResult(Option<JobTree>.None);

    public ValueTask<Result<JobHold, JobRejection>> HoldAsync(JobId job, HoldReason reason, CancellationToken cancellationToken) => RefusedAsync<JobHold>();

    public ValueTask<Result<JobContinuation, JobRejection>> ContinueAsync(JobId job, string message, CancellationToken cancellationToken) => RefusedAsync<JobContinuation>();

    public ValueTask<Result<JobContinuation, JobRejection>> ContinueOnAsync(JobId job, ConnectionName connection, string message, CancellationToken cancellationToken) => RefusedAsync<JobContinuation>();

    public ValueTask<Result<JobSteered, JobRejection>> SteerAsync(JobId job, string message, CancellationToken cancellationToken) => RefusedAsync<JobSteered>();

    public ValueTask<Result<JobId, JobRejection>> DiscardAsync(JobId job, CancellationToken cancellationToken) => RefusedAsync<JobId>();

    public ValueTask<Result<JobApproval, JobRejection>> ApproveAsync(JobId job, CancellationToken cancellationToken) => RefusedAsync<JobApproval>();

    public ValueTask<Result<JobContinuation, JobRejection>> SendBackAsync(JobId job, string feedback, CancellationToken cancellationToken) => RefusedAsync<JobContinuation>();

    public ValueTask<Result<JobContinuation, JobRejection>> ResumeAsync(JobId job, CancellationToken cancellationToken) => RefusedAsync<JobContinuation>();

    public ValueTask<Result<JobContinuation, JobRejection>> HandOffAsync(JobId job, JobHandoff handoff, CancellationToken cancellationToken) => RefusedAsync<JobContinuation>();

    private static ValueTask<Result<T, JobRejection>> RefusedAsync<T>() => ValueTask.FromResult(Result<T, JobRejection>.Failure(JobRejection.UnknownJob));
}

internal sealed class FakePolicies : IRepositoryPolicies
{
    public Option<Autonomy> Declared { get; set; }

    public ValueTask<RepositoryPolicy> OfRepositoryAsync(string repository, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Declared.Match(
            level => new RepositoryPolicy(PolicyFileStatus.Applied, Option<PolicyError>.None, [], Option<Workspaces.Contracts.FileOrigin>.None) { Autonomy = level },
            () => new RepositoryPolicy(PolicyFileStatus.Absent, Option<PolicyError>.None, [], Option<Workspaces.Contracts.FileOrigin>.None)));
}

internal sealed class FakeAutopilot : IAutopilot
{
    private readonly ConcurrentQueue<LoopRequest> started = new();

    public IReadOnlyList<LoopRequest> Started => [.. started];

    public ValueTask<Result<LoopId, AutopilotError>> StartAsync(LoopRequest request, CancellationToken cancellationToken)
    {
        started.Enqueue(request);

        return ValueTask.FromResult(Result<LoopId, AutopilotError>.Success(LoopId.New()));
    }

    public ValueTask<Result<LoopId, AutopilotError>> PauseAsync(LoopId loop, CancellationToken cancellationToken) => UnknownAsync();

    public ValueTask<Result<LoopId, AutopilotError>> ResumeAsync(LoopId loop, CancellationToken cancellationToken) => UnknownAsync();

    public ValueTask<Result<LoopId, AutopilotError>> StopAsync(LoopId loop, CancellationToken cancellationToken) => UnknownAsync();

    public IReadOnlyList<LoopState> Loops() =>
        [.. Started.Select(request => new LoopState(LoopId.New(), request.Repository, LoopStatus.Running, DateTimeOffset.UnixEpoch, 0, Option<JobId>.None))];

    public Option<LoopDigest> DigestOf(LoopId loop) => Option<LoopDigest>.None;

    private static ValueTask<Result<LoopId, AutopilotError>> UnknownAsync() => ValueTask.FromResult(Result<LoopId, AutopilotError>.Failure(AutopilotError.UnknownLoop));
}

internal sealed class FakeSecrets : ISecrets
{
    public Dictionary<string, string> Values { get; } = [];

    public Option<string> Of(string variable) => Values.TryGetValue(variable, out var value) ? value : Option<string>.None;
}

internal sealed class FakeEndpoint : IWebhookEndpoint
{
    public WebhookEndpoint Current { get; private set; } = WebhookEndpoint.Off;

    public Task RefreshAsync(CatalogSnapshot snapshot, CancellationToken cancellationToken)
    {
        Current = snapshot.Triggers.Any(trigger => trigger.Webhook.IsSome) ? new WebhookEndpoint("http://localhost:24000/hooks/", Option<TriggerError>.None) : WebhookEndpoint.Off;

        return Task.CompletedTask;
    }
}
