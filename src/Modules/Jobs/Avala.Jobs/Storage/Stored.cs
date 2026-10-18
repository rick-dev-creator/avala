using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Jobs.Jobs;
using Avala.Sdk;
using Avala.Storage;

namespace Avala.Jobs.Storage;

internal static class Stored
{
    public static Instruction Instruction(string text) => Restore(Avala.Jobs.Jobs.Instruction.Create(text));

    public static Feedback Feedback(string text) => Restore(Avala.Jobs.Jobs.Feedback.Create(text));

    public static AttemptBudget Budget(int attemptsPerRound) => Restore(AttemptBudget.Create(attemptsPerRound));

    public static RepositoryPath Repository(string path) => Restore(RepositoryPath.Create(path));

    public static string Text(ModelChoice choice) => choice.IsDefault ? string.Empty : StoredJson.Write(choice);

    public static string Text(Option<HoldReason> hold) => hold.Match(reason => reason.ToString(), () => string.Empty);

    public static string Text(Option<JobFailure> failure) => failure.Match(reason => reason.ToString(), () => string.Empty);

    public static string Text(Option<string> problem) => problem.Match(found => found, () => string.Empty);

    public static string Text(IReadOnlyList<Attachment> attachments) => attachments.Count == 0 ? string.Empty : StoredJson.Write(attachments);

    public static Option<string> Problem(string text) => text.Length == 0 ? Option<string>.None : text;

    public static ModelChoice Model(string text) => text.Length == 0 ? ModelChoice.Default : StoredJson.Read<ModelChoice>(text);

    public static Option<HoldReason> Hold(string text) => text.Length == 0 ? Option<HoldReason>.None : Enum.Parse<HoldReason>(text);
    public static Option<JobFailure> Failure(string text) => text.Length == 0 ? Option<JobFailure>.None : Enum.Parse<JobFailure>(text);

    public static IReadOnlyList<Attachment> Attachments(string text) => text.Length == 0 ? [] : StoredJson.Read<List<Attachment>>(text);

    private static T Restore<T>(Result<T, JobError> stored) =>
        stored.Match(value => value, error => throw new InvalidDataException($"Stored job data is invalid: {error}"));
}
