using Ardalis.Result;
using Mediator;

namespace Avala.Server.Instances.Contracts.Lookup;

internal sealed record FindInstance(Guid Id) : IQuery<Result<InstanceDetails>>;
