using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Jobs.Jobs;

internal sealed record Feedback
{
    private Feedback(string text) => Text = text;

    public string Text { get; }

    public IReadOnlyList<Attachment> Attachments { get; private init; } = [];

    public MessageAuthor Author { get; private init; }

    public Feedback With(IReadOnlyList<Attachment> attachments) => this with { Attachments = attachments };

    public Feedback By(MessageAuthor author) => this with { Author = author };

    public static Result<Feedback, JobError> Create(string text) =>
        string.IsNullOrWhiteSpace(text) ? JobError.EmptyFeedback : new Feedback(text.Trim());
}
