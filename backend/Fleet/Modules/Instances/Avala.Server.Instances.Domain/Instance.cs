namespace Avala.Server.Instances.Domain;

internal sealed class Instance
{
    public const int NameLength = 100;

    private Instance(InstanceId id, string name, DateTimeOffset registeredAt)
    {
        Id = id;
        Name = name;
        RegisteredAt = registeredAt;
    }

    public InstanceId Id { get; }

    public string Name { get; }

    public DateTimeOffset RegisteredAt { get; }

    public static Instance Register(string name, DateTimeOffset at) => new(InstanceId.New(), name.Trim(), at);
}
