using Avala.Delegation.Contracts;
using Avala.Workbench.Overview;

namespace Avala.Workbench.Tests.Overview;

public sealed class OverviewPhrasesTests
{
    [Theory]
    [InlineData(ChildOutcome.Integrated, "integrated into its parent")]
    [InlineData(ChildOutcome.Conflict, "conflicts with its parent")]
    [InlineData(ChildOutcome.NotIntegrated, "not integrated")]
    [InlineData(ChildOutcome.Held, "held")]
    [InlineData(ChildOutcome.RetriesExhausted, "retries ran out")]
    [InlineData(ChildOutcome.Failed, "failed")]
    [InlineData(ChildOutcome.Discarded, "discarded")]
    public void AChildsOutcomeIsPhrasedForItsParent(ChildOutcome outcome, string phrase) =>
        Assert.Equal(phrase, OverviewPhrases.Outcome(outcome));

    [Theory]
    [InlineData(DelegationError.NotDeclared, "the repository declares no delegation to that connection")]
    [InlineData(DelegationError.DepthExceeded, "too deep")]
    [InlineData(DelegationError.TooManyChildren, "too many children")]
    [InlineData(DelegationError.AutonomyLoosened, "it asked for more autonomy than its parent")]
    [InlineData(DelegationError.NotSubmitted, "the child job was refused")]
    [InlineData(DelegationError.MalformedInput, "the call was malformed")]
    [InlineData(DelegationError.NoJob, "the caller is not a job")]
    [InlineData(DelegationError.Unreadable, "the delegation rules cannot be read")]
    [InlineData(DelegationError.InvalidDepth, "the delegation rules cannot be read")]
    public void ARefusedDelegationSaysWhyItWasRefused(DelegationError error, string phrase) =>
        Assert.Equal(phrase, OverviewPhrases.Refusal(error));
}
