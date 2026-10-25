namespace Avala.Server.Instances.Contracts.Registration;

internal sealed record RegisteredInstance(Guid Id, string Name, DateTimeOffset RegisteredAt);
