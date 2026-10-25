namespace Avala.Server.Instances.Domain;

internal readonly record struct InstanceId(Guid Value)
{
    public static InstanceId New() => new(Guid.CreateVersion7());
}
