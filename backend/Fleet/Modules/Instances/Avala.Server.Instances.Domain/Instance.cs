namespace Avala.Server.Instances.Domain;

internal sealed class Instance
{
    public const int NameLength = 100;

    private Instance(InstanceId id, string name, string keyHash, DateTimeOffset registeredAt)
    {
        Id = id;
        Name = name;
        KeyHash = keyHash;
        RegisteredAt = registeredAt;
    }

    public InstanceId Id { get; }

    public string Name { get; }

    public string KeyHash { get; }

    public DateTimeOffset RegisteredAt { get; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public static IssuedInstance Register(string name, DateTimeOffset at)
    {
        var key = InstanceKey.Issue();

        return new IssuedInstance(new Instance(InstanceId.New(), name.Trim(), InstanceKey.Hash(key), at), key);
    }

    public void Revoke(DateTimeOffset at) => RevokedAt ??= at;
}
