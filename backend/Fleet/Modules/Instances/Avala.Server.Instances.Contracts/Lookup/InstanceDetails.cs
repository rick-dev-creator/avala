namespace Avala.Server.Instances.Contracts.Lookup;

internal sealed record InstanceDetails(Guid Id, string Name, DateTimeOffset RegisteredAt, DateTimeOffset? RevokedAt);
