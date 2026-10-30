using Avala.Server.Instances.Contracts.Authentication;

namespace Avala.Server.Instances.Contracts.Registration;

internal sealed record RegisteredInstance(Guid Id, string Name, DateTimeOffset RegisteredAt, string Key)
{
    public override string ToString() =>
        $"{nameof(RegisteredInstance)} {{ Id = {Id}, Name = {Name}, RegisteredAt = {RegisteredAt:O}, Key = {InstanceKeyShape.Masked} }}";
}
