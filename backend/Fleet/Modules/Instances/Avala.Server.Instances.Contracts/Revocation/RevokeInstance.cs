using Ardalis.Result;
using Mediator;

namespace Avala.Server.Instances.Contracts.Revocation;

internal sealed record RevokeInstance(Guid Id) : ICommand<Result>;
