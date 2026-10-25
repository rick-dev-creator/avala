using Ardalis.Result;
using Avala.Server.Instances.Contracts.Registration;
using Avala.Server.Instances.Domain;
using Avala.Server.Instances.Infrastructure.Storage;
using Mediator;

namespace Avala.Server.Instances.Infrastructure.Registration;

internal sealed class RegisterInstanceHandler(InstancesDbContext database, TimeProvider clock)
    : ICommandHandler<RegisterInstance, Result<RegisteredInstance>>
{
    public async ValueTask<Result<RegisteredInstance>> Handle(RegisterInstance command, CancellationToken cancellationToken)
    {
        var issued = Instance.Register(command.Name, clock.GetUtcNow());
        await database.Instances.AddAsync(issued.Instance, cancellationToken);
        await database.SaveChangesAsync(cancellationToken);

        return Result<RegisteredInstance>.Created(
            new RegisteredInstance(issued.Instance.Id.Value, issued.Instance.Name, issued.Instance.RegisteredAt, issued.Key));
    }
}
