namespace Avala.Agents.Contracts.Sessions;

public readonly record struct SessionId(Guid Value)
{
    public static SessionId New() => new(Guid.CreateVersion7());
}

public readonly record struct TurnId(Guid Value)
{
    public static TurnId New() => new(Guid.CreateVersion7());
}

public readonly record struct ItemId(string Value);
