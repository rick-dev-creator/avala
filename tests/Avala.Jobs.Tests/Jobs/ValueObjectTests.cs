using Avala.Jobs.Contracts;
using Avala.Jobs.Jobs;
using Avala.Testing;

namespace Avala.Jobs.Tests.Jobs;

public sealed class ValueObjectTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ARepositoryPathNeedsText(string path) =>
        Assert.Equal(JobError.EmptyRepository, Outcomes.FailsWith(RepositoryPath.Create(path)));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnInstructionNeedsText(string text) =>
        Assert.Equal(JobError.EmptyInstruction, Outcomes.FailsWith(Instruction.Create(text)));

    [Fact]
    public void AnInstructionIsTrimmed() =>
        Assert.Equal("Add GitHub login", Outcomes.Succeeds(Instruction.Create("  Add GitHub login ")).Text);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void FeedbackNeedsText(string text) =>
        Assert.Equal(JobError.EmptyFeedback, Outcomes.FailsWith(Feedback.Create(text)));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ABudgetAllowsAtLeastOneAttempt(int attemptsPerRound) =>
        Assert.Equal(JobError.InvalidAttemptBudget, Outcomes.FailsWith(AttemptBudget.Create(attemptsPerRound)));

    [Fact]
    public void AttemptsAreNumberedFromOne() =>
        Assert.Equal([1, 2, 3], new[] { AttemptNumber.First, AttemptNumber.First.Next, AttemptNumber.First.Next.Next }.Select(number => number.Value));

    [Fact]
    public void EveryJobGetsItsOwnIdentifier() =>
        Assert.NotEqual(JobId.New(), JobId.New());
}
