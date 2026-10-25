using System.Data;
using Ardalis.Result;
using Avala.Server.Activity.Contracts.Ingestion;
using Avala.Server.Activity.Infrastructure.Storage;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Avala.Server.Activity.Infrastructure.Ingestion;

internal sealed class IngestEventsHandler(ActivityDbContext database, TimeProvider clock, ILogger<IngestEventsHandler> logger)
    : ICommandHandler<IngestEvents, Result<IngestionReceipt>>
{
    public async ValueTask<Result<IngestionReceipt>> Handle(IngestEvents command, CancellationToken cancellationToken)
    {
        var receivedAt = clock.GetUtcNow();
        var batch = new IngestionBatch(command.Events, receivedAt);
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await database.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock({ActivityLocks.Ingestion}, {ActivityLocks.Of(command.Instance)})",
            cancellationToken);

        var ids = batch.Ids;
        batch.Sort((await database.ReceivedEvents
            .Where(received => received.InstanceId == command.Instance && ids.Contains(received.EventId))
            .Select(received => received.EventId)
            .ToListAsync(cancellationToken)).ToHashSet());

        var state = await IngestionState.LoadAsync(database, command, receivedAt, batch, cancellationToken);
        var received = new EventApplication(command.Instance, state).ApplyAll(batch, receivedAt);
        state.Instance.See(command.InstanceName, receivedAt);
        await state.AddNewAsync(database, received, cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        if (batch.Clamped > 0)
        {
            IngestionLog.ClockAhead(logger, command.Instance, batch.Clamped);
        }

        return batch.Receipt();
    }
}
