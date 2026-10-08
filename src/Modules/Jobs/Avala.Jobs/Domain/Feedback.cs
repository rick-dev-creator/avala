using Avala.Sdk;

namespace Avala.Jobs.Domain;

internal sealed record Feedback
{
    private Feedback(string text) => Text = text;

    public string Text { get; }

    public static Result<Feedback, JobError> Create(string text) =>
        string.IsNullOrWhiteSpace(text) ? JobError.EmptyFeedback : new Feedback(text.Trim());
}
