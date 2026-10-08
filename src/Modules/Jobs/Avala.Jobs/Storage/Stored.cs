using Avala.Jobs.Jobs;
using Avala.Sdk;

namespace Avala.Jobs.Storage;

internal static class Stored
{
    public static Instruction Instruction(string text) => Restore(Avala.Jobs.Jobs.Instruction.Create(text));

    public static Feedback Feedback(string text) => Restore(Avala.Jobs.Jobs.Feedback.Create(text));

    public static AttemptBudget Budget(int attemptsPerRound) => Restore(AttemptBudget.Create(attemptsPerRound));

    public static RepositoryPath Repository(string path) => Restore(RepositoryPath.Create(path));

    private static T Restore<T>(Result<T, JobError> stored) =>
        stored.Match(value => value, error => throw new InvalidDataException($"Stored job data is invalid: {error}"));
}
