namespace Avala.Agents.Contracts.Connections;

public readonly record struct ConnectionName(string Value);

public readonly record struct Secret(string Value)
{
    public override string ToString() => "[secret]";
}
