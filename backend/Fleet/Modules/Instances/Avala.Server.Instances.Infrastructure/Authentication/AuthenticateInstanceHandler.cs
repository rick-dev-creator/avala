using Ardalis.Result;
using Avala.Server.Instances.Contracts.Authentication;
using Avala.Server.Instances.Domain;
using Avala.Server.Instances.Infrastructure.Storage;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Avala.Server.Instances.Infrastructure.Authentication;

internal sealed class AuthenticateInstanceHandler(InstancesDbContext database)
    : IQueryHandler<AuthenticateInstance, Result<AuthenticatedInstance>>
{
    public async ValueTask<Result<AuthenticatedInstance>> Handle(AuthenticateInstance query, CancellationToken cancellationToken)
    {
        var hash = InstanceKey.Hash(query.Key);
        var instance = await database.Instances.AsNoTracking()
            .Active()
            .SingleOrDefaultAsync(found => found.KeyHash == hash, cancellationToken);

        return instance is null
            ? Result<AuthenticatedInstance>.Unauthorized()
            : new AuthenticatedInstance(instance.Id.Value, instance.Name);
    }
}
