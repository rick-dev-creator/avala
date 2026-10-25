using Ardalis.Result;
using Mediator;

namespace Avala.Server.Instances.Contracts.Registration;

internal sealed record RegisterInstance(string Name) : ICommand<Result<RegisteredInstance>>;
