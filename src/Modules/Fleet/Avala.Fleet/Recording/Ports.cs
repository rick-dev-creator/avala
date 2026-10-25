using Avala.Fleet.Delivery;
using Avala.Fleet.Facts;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Fleet.Recording;

internal sealed record StoredFact(Guid Id, string Kind, string Body);

internal sealed record RejectionNote(string Kind, string Reason, DateTimeOffset At);

internal sealed record OutboxSummary(int Waiting, Option<DateTimeOffset> LastDelivery, IReadOnlyList<RejectionNote> Rejections, int Dropped);

internal interface IFactStore
{
    Task<bool> IsAnnouncedAsync(string scope, Guid job, CancellationToken cancellationToken);

    Task AnnounceAsync(string scope, Guid job, bool keyless, CancellationToken cancellationToken);

    Task AppendAsync(string scope, Fact fact, CancellationToken cancellationToken);

    Task<DateTimeOffset> LatestStampAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<StoredFact>> DueAsync(string scope, DateTimeOffset now, int limit, CancellationToken cancellationToken);

    Task<Option<DateTimeOffset>> NextDueAsync(string scope, CancellationToken cancellationToken);

    Task SettleAsync(string scope, ReceiptPlan plan, DateTimeOffset now, CancellationToken cancellationToken);

    Task<int> ExpireAsync(string scope, DateTimeOffset now, CancellationToken cancellationToken);

    Task<OutboxSummary> SummaryAsync(string scope, CancellationToken cancellationToken);

    Task KeepOnlyAsync(string scope, CancellationToken cancellationToken);

    Task ForgetAsync(CancellationToken cancellationToken);
}

internal sealed record JobAnnouncement(Option<string> Repository, Option<Guid> Parent, Option<string> Model, Option<string> Effort, DateTimeOffset Submitted);

internal interface IJobFacts
{
    ValueTask<Option<bool>> IsScratchAsync(JobId job, CancellationToken cancellationToken);

    ValueTask<Option<JobAnnouncement>> AnnouncementAsync(JobId job, CancellationToken cancellationToken);
}
