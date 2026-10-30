using Ardalis.Result;
using Mediator;

namespace Avala.Server.Instances.Contracts.Authentication;

internal sealed record AuthenticateInstance(string Key) : IQuery<Result<AuthenticatedInstance>>
{
    public override string ToString() => $"{nameof(AuthenticateInstance)} {{ Key = {InstanceKeyShape.Masked} }}";
}
