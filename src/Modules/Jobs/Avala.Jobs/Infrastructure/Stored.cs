using Avala.Jobs.Domain;
using Avala.Sdk;

namespace Avala.Jobs.Infrastructure;

internal static class Stored
{
    public static Instruction Instruction(string text) => Restore(Avala.Jobs.Domain.Instruction.Create(text));

    public static Feedback Feedback(string text) => Restore(Avala.Jobs.Domain.Feedback.Create(text));

    public static AttemptBudget Budget(int attemptsPerRound) => Restore(AttemptBudget.Create(attemptsPerRound));

    public static RepositoryPath Repository(string path) => Restore(RepositoryPath.Create(path));

    private static T Restore<T>(Result<T, JobError> stored) =>
        stored.Match(value => value, error => throw new InvalidDataException($"Stored job data is invalid: {error}"));
}
