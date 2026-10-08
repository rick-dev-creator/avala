using Avala.Sdk;

namespace Avala.Jobs.Domain;

internal sealed record AttemptBudget
{
    private AttemptBudget(int attemptsPerRound) => AttemptsPerRound = attemptsPerRound;

    public int AttemptsPerRound { get; }

    public static Result<AttemptBudget, JobError> Create(int attemptsPerRound) =>
        attemptsPerRound < 1 ? JobError.InvalidAttemptBudget : new AttemptBudget(attemptsPerRound);
}
