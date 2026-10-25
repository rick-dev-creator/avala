using System.Data;
using Ardalis.Result;
using Avala.Server.Activity.Contracts;
using Avala.Server.Activity.Contracts.Usage;
using Avala.Server.Activity.Domain;
using Avala.Server.Activity.Infrastructure.Storage;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Avala.Server.Activity.Infrastructure.Usage;

internal sealed class ListUsageHandler(ActivityDbContext database) : IQueryHandler<ListUsage, Result<UsageReport>>
{
    public async ValueTask<Result<UsageReport>> Handle(ListUsage query, CancellationToken cancellationToken)
    {
        var reading = UsageRequests.Read(query.From, query.To, query.Zone, query.Step, query.Repository);

        if (reading.Request is not { } request)
        {
            return Result<UsageReport>.Invalid(reading.Refusals.Select(refusal => new ValidationError(refusal.Field, refusal.Reason)).ToList());
        }

        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var sums = await new UsageSums(database, request).ReadAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return UsageAssembly.Report(request, sums);
    }
}
