namespace Avala.Agents.Contracts.Sessions;

public enum MessageAuthor
{
    Person,
    Avala,
}

public sealed record UserTurn(string Text)
{
    public bool MidTurn { get; init; }

    public IReadOnlyList<MessageId> Messages { get; init; } = [];

    public IReadOnlyList<Attachment> Attachments { get; init; } = [];

    public MessageAuthor Author { get; init; } = MessageAuthor.Person;
}
