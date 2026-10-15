namespace Avala.Agents.Contracts.Sessions;

public sealed record UserTurn(string Text)
{
    public bool MidTurn { get; init; }

    public IReadOnlyList<Attachment> Attachments { get; init; } = [];
}
