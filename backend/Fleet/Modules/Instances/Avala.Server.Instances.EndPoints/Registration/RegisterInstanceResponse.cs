namespace Avala.Server.Instances.EndPoints.Registration;

internal sealed record RegisterInstanceResponse(Guid Id, string Name, DateTimeOffset RegisteredAt);
