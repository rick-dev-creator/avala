using Avala.Sdk;

namespace Avala.Agents.Contracts.Sessions;

public sealed record FieldAnswer(string Field)
{
    public IReadOnlyList<string> Chosen { get; init; } = [];

    public Option<string> Text { get; init; }

    public bool Confirmed { get; init; }
}

public sealed record FormAnswer(ItemId Item, IReadOnlyList<FieldAnswer> Fields)
{
    public bool Declined { get; init; }

    public Option<string> Message { get; init; }
}
