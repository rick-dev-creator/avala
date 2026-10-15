using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;

namespace Avala.Jobs.Jobs;

internal sealed record Feedback
{
    private Feedback(string text) => Text = text;

    public string Text { get; }

    public IReadOnlyList<Attachment> Attachments { get; private init; } = [];

    public Feedback With(IReadOnlyList<Attachment> attachments) => this with { Attachments = attachments };

    public static Result<Feedback, JobError> Create(string text) =>
        string.IsNullOrWhiteSpace(text) ? JobError.EmptyFeedback : new Feedback(text.Trim());
}
