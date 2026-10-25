namespace Avala.Server.Instances.EndPoints.Lookup;

internal sealed record FindInstanceResponse(Guid Id, string Name, DateTimeOffset RegisteredAt, DateTimeOffset? RevokedAt);
