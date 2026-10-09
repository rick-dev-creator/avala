using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Board;
using Avala.Workbench.Sidebar;

namespace Avala.Workbench.Tests.Sidebar;

public sealed class FactPhrasesTests
{
    [Theory]
    [InlineData("Starting", "starting")]
    [InlineData("Working", "working")]
    [InlineData("Verifying", "verifying")]
    [InlineData("AsksQuestion", "asks a question")]
    [InlineData("AsksPlanApproval", "asks to approve a plan")]
    [InlineData("AsksForInput", "asks for input")]
    [InlineData("NeedsHelp", "needs help: retries ran out")]
    [InlineData("NoChecks", "no checks declared")]
    [InlineData("ReadyForReview", "ready for review")]
    [InlineData("Discarded", "discarded")]
    [InlineData("Failed", "failed")]
    public void AFactWithoutDetailsIsPhrasedByItsKind(string kind, string phrase) =>
        Assert.Equal(phrase, FactPhrases.Of(new JobFact(Enum.Parse<FactKind>(kind))));

    [Fact]
    public void PlanProgressCountsTheDoneSteps() =>
        Assert.Equal("3 of 5", FactPhrases.Of(new JobFact(FactKind.PlanProgress) { Done = 3, Total = 5 }));

    [Theory]
    [InlineData("FileEdit", "wants to edit a file")]
    [InlineData("Web", "wants to reach the web")]
    [InlineData("Mcp", "wants to use a tool")]
    [InlineData("Search", "asks permission")]
    [InlineData("", "asks permission")]
    public void APermissionRequestNamesWhatTheAgentWants(string item, string phrase) =>
        Assert.Equal(
            phrase,
            FactPhrases.Of(new JobFact(FactKind.AsksPermission)
            {
                Item = item.Length == 0 ? Option<ItemKind>.None : Enum.Parse<ItemKind>(item),
            }));

    [Theory]
    [InlineData("SessionLost", "held: session lost")]
    [InlineData("BudgetExceeded", "held: over budget")]
    [InlineData("LimitNearlyReached", "held: near its usage limit")]
    [InlineData("InvalidBudget", "held: invalid budget")]
    [InlineData("MemoryExceeded", "held: out of memory")]
    [InlineData("Stopped", "stopped")]
    [InlineData("Interrupted", "interrupted")]
    [InlineData("", "held")]
    public void AHoldNamesItsReason(string reason, string phrase) =>
        Assert.Equal(
            phrase,
            FactPhrases.Of(new JobFact(FactKind.Held)
            {
                Hold = reason.Length == 0 ? Option<HoldReason>.None : Enum.Parse<HoldReason>(reason),
            }));

    [Theory]
    [InlineData(1, "verified")]
    [InlineData(3, "verified on attempt 3")]
    public void AVerificationAfterRetriesNamesItsAttempt(int attempt, string phrase) =>
        Assert.Equal(phrase, FactPhrases.Of(new JobFact(FactKind.Verified) { Attempt = attempt }));

    [Theory]
    [InlineData("merge", "merged")]
    [InlineData("branch", "approved")]
    [InlineData("", "approved")]
    public void AnApprovalSaysWhetherTheWorkWasMerged(string strategy, string phrase) =>
        Assert.Equal(
            phrase,
            FactPhrases.Of(new JobFact(FactKind.Approved)
            {
                Strategy = strategy.Length == 0 ? Option<string>.None : strategy,
            }));

    [Fact]
    public void ATitleIsTheFirstLineOfTheInstructionCutShort()
    {
        var title = FactPhrases.Title($"{new string('a', 100)}\nMore detail");

        Assert.Equal((80, '…'), (title.Length, title[^1]));
    }
}
