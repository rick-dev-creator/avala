using Avala.Server.Activity.Contracts.Ingestion;
using Avala.Server.Activity.Domain;
using Avala.Server.Activity.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace Avala.Server.Activity.Infrastructure.Ingestion;

internal sealed class IngestionState
{
    private readonly List<FleetJob> started = [];

    private readonly List<QuarterUsage> created = [];

    private readonly Dictionary<Guid, FleetJob> jobs;

    private readonly Dictionary<(Guid Job, DateTimeOffset Quarter, string Model), QuarterUsage> buckets;

    private readonly bool registered;

    private IngestionState(
        Dictionary<Guid, FleetJob> jobs,
        FleetInstance instance,
        bool registered,
        Dictionary<(Guid Job, DateTimeOffset Quarter, string Model), QuarterUsage> buckets)
    {
        this.jobs = jobs;
        Instance = instance;
        this.registered = registered;
        this.buckets = buckets;
    }

    public FleetInstance Instance { get; }

    internal IngestionState(FleetInstance instance)
        : this([], instance, registered: true, [])
    {
    }

    public static async Task<IngestionState> LoadAsync(
        ActivityDbContext database,
        IngestEvents command,
        DateTimeOffset receivedAt,
        IngestionBatch batch,
        CancellationToken cancellationToken)
    {
        var jobIds = batch.JobIds;
        var jobs = await database.Jobs
            .Where(job => job.InstanceId == command.Instance && jobIds.Contains(job.JobId))
            .ToDictionaryAsync(job => job.JobId, cancellationToken);
        var instance = await database.Instances.SingleOrDefaultAsync(found => found.Id == command.Instance, cancellationToken);
        var (bucketJobs, quarters) = batch.UsageKeys;
        var buckets = bucketJobs.Count == 0
            ? []
            : await database.Quarters
                .Where(usage => usage.InstanceId == command.Instance && bucketJobs.Contains(usage.JobId) && quarters.Contains(usage.Quarter))
                .ToDictionaryAsync(usage => (usage.JobId, usage.Quarter, usage.Model), cancellationToken);

        return new IngestionState(
            jobs,
            instance ?? FleetInstance.Register(command.Instance, command.InstanceName, receivedAt),
            instance is null,
            buckets);
    }

    public FleetJob? Known(Guid id) => jobs.GetValueOrDefault(id);

    public FleetJob JobFor(Guid instanceId, Guid id, EventTime at)
    {
        if (!jobs.TryGetValue(id, out var job))
        {
            job = FleetJob.Start(instanceId, id, at);
            jobs[id] = job;
            started.Add(job);
        }

        return job;
    }

    public QuarterUsage BucketFor(Guid instanceId, Guid job, DateTimeOffset quarter, string model)
    {
        if (!buckets.TryGetValue((job, quarter, model), out var bucket))
        {
            bucket = QuarterUsage.Start(instanceId, job, quarter, model);
            buckets[(job, quarter, model)] = bucket;
            created.Add(bucket);
        }

        return bucket;
    }

    public async Task AddNewAsync(ActivityDbContext database, IReadOnlyList<ReceivedEvent> received, CancellationToken cancellationToken)
    {
        if (registered)
        {
            await database.Instances.AddAsync(Instance, cancellationToken);
        }

        await database.Jobs.AddRangeAsync(started, cancellationToken);
        await database.Quarters.AddRangeAsync(created, cancellationToken);
        await database.ReceivedEvents.AddRangeAsync(received, cancellationToken);
    }
}
