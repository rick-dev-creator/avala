using Avala.Contests.Contracts;
using Avala.Jobs.Contracts;
using Avala.Workbench.Presenting;

namespace Avala.Workbench.Tests.Inspector;

public sealed class ContestPhrasesTests
{
    [Theory]
    [InlineData(Criterion.Exceptions, "ahead of the next on fewer evidence exceptions")]
    [InlineData(Criterion.Attempts, "ahead of the next on fewer attempts")]
    [InlineData(Criterion.DiffSize, "ahead of the next on a smaller diff")]
    [InlineData(Criterion.Cost, "ahead of the next on a lower cost")]
    [InlineData(Criterion.Finish, "ahead of the next on an earlier finish")]
    [InlineData(Criterion.Order, "ahead of the next on the order it started in")]
    public void AVerifiedRunAheadOfTheNextSaysWhichCriterionPutItThere(Criterion criterion, string reason) =>
        Assert.Equal(reason, ContestPhrases.Reason(new Standing(1, new CandidateFacts(JobId.New(), 1, Passed: true, RuleFileEdited: false, 0, 1, 2, DateTimeOffset.UnixEpoch)) { Ahead = criterion }));
}
