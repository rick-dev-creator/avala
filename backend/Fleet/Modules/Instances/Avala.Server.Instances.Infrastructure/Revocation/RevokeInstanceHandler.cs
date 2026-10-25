using Ardalis.Result;
using Avala.Server.Instances.Contracts.Revocation;
using Avala.Server.Instances.Domain;
using Avala.Server.Instances.Infrastructure.Storage;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Avala.Server.Instances.Infrastructure.Revocation;

internal sealed class RevokeInstanceHandler(InstancesDbContext database, TimeProvider clock) : ICommandHandler<RevokeInstance, Result>
{
    public async ValueTask<Result> Handle(RevokeInstance command, CancellationToken cancellationToken)
    {
        var id = new InstanceId(command.Id);
        var instance = await database.Instances.SingleOrDefaultAsync(found => found.Id == id, cancellationToken);

        if (instance is null)
        {
            return Result.NotFound();
        }

        instance.Revoke(clock.GetUtcNow());
        await database.SaveChangesAsync(cancellationToken);

        return Result.NoContent();
    }
}
