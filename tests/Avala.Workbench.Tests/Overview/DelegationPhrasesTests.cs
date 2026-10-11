using Avala.Delegation.Contracts;
using Avala.Workbench.Presenting;

namespace Avala.Workbench.Tests.Overview;

public sealed class DelegationPhrasesTests
{
    [Theory]
    [InlineData(ChildOutcome.Integrated, "integrated into its parent")]
    [InlineData(ChildOutcome.Conflict, "conflicts with its parent")]
    [InlineData(ChildOutcome.NotIntegrated, "not integrated")]
    [InlineData(ChildOutcome.Held, "held")]
    [InlineData(ChildOutcome.RetriesExhausted, "retries ran out")]
    [InlineData(ChildOutcome.Failed, "failed")]
    [InlineData(ChildOutcome.Discarded, "discarded")]
    [InlineData(ChildOutcome.Reported, "reported its findings")]
    public void AChildsOutcomeIsPhrasedForItsParent(ChildOutcome outcome, string phrase) =>
        Assert.Equal(phrase, DelegationPhrases.Outcome(outcome));

    [Theory]
    [InlineData(DelegationError.NotDeclared, "the repository declares no delegation to that connection")]
    [InlineData(DelegationError.DepthExceeded, "too deep")]
    [InlineData(DelegationError.TooManyChildren, "too many children")]
    [InlineData(DelegationError.AutonomyLoosened, "it asked for more autonomy than its parent")]
    [InlineData(DelegationError.NotSubmitted, "the child job was refused")]
    [InlineData(DelegationError.MalformedInput, "the call was malformed")]
    [InlineData(DelegationError.NoJob, "the caller is not a job")]
    [InlineData(DelegationError.RoleLoosened, "it asked for a worker where children are read-only")]
    [InlineData(DelegationError.Unreadable, "the delegation rules cannot be read")]
    [InlineData(DelegationError.InvalidDepth, "the delegation rules cannot be read")]
    public void ARefusedDelegationSaysWhyItWasRefused(DelegationError error, string phrase) =>
        Assert.Equal(phrase, DelegationPhrases.Refusal(error));
}
