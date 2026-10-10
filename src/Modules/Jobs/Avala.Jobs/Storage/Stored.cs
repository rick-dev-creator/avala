using Avala.Agents.Contracts.Sessions;
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

    public static ModelChoice Model(string text) => text.Length == 0 ? ModelChoice.Default : StoredJson.Read<ModelChoice>(text);

    private static T Restore<T>(Result<T, JobError> stored) =>
        stored.Match(value => value, error => throw new InvalidDataException($"Stored job data is invalid: {error}"));
}
