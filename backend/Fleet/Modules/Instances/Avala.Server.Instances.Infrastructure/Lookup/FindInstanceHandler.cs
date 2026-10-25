using Ardalis.Result;
using Avala.Server.Instances.Contracts.Lookup;
using Avala.Server.Instances.Domain;
using Avala.Server.Instances.Infrastructure.Storage;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Avala.Server.Instances.Infrastructure.Lookup;

internal sealed class FindInstanceHandler(InstancesDbContext database) : IQueryHandler<FindInstance, Result<InstanceDetails>>
{
    public async ValueTask<Result<InstanceDetails>> Handle(FindInstance query, CancellationToken cancellationToken)
    {
        var id = new InstanceId(query.Id);
        var instance = await database.Instances.AsNoTracking().SingleOrDefaultAsync(found => found.Id == id, cancellationToken);

        return instance is null
            ? Result<InstanceDetails>.NotFound()
            : new InstanceDetails(instance.Id.Value, instance.Name, instance.RegisteredAt);
    }
}
