using Avala.Server.Instances.Contracts.Authentication;

namespace Avala.Server.Instances.EndPoints.Registration;

internal sealed record RegisterInstanceResponse(Guid Id, string Name, DateTimeOffset RegisteredAt, string Key)
{
    public override string ToString() =>
        $"{nameof(RegisterInstanceResponse)} {{ Id = {Id}, Name = {Name}, RegisteredAt = {RegisteredAt:O}, Key = {InstanceKeyShape.Masked} }}";
}
