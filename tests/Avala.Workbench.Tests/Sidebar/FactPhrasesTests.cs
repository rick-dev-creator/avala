using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Handoffs.Contracts;
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
    [InlineData("WaitsForParent", "waiting for its parent")]
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
    [InlineData("InvalidChecks", "held: invalid checks")]
    [InlineData("MemoryExceeded", "held: out of memory")]
    [InlineData("Stopped", "stopped")]
    [InlineData("CouldNotStart", "could not start")]
    [InlineData("Interrupted", "interrupted")]
    [InlineData("", "held")]
    public void AHoldNamesItsReason(string reason, string phrase) =>
        Assert.Equal(
            phrase,
            FactPhrases.Of(new JobFact(FactKind.Held)
            {
                Hold = reason.Length == 0 ? Option<HoldReason>.None : Enum.Parse<HoldReason>(reason),
            }));

    [Fact]
    public void AJobWaitingForAResetSaysWhenItResumes()
    {
        var since = new DateTimeOffset(2026, 10, 10, 13, 0, 0, TimeSpan.Zero);
        var resumes = since.AddHours(2).AddMinutes(51);
        var wait = new ResetWait(JobId.New(), new ConnectionName("claude-work"), "5h", resumes, since);

        Assert.Equal($"resumes at {resumes.ToLocalTime():HH:mm}", FactPhrases.Of(new JobFact(FactKind.ResumesAtReset) { Wait = wait }));
    }

    [Fact]
    public void AJobThatWaitsForThePersonSaysWhenItsWindowResetsAndThenThatItWaitsForThem()
    {
        var since = new DateTimeOffset(2026, 10, 10, 13, 0, 0, TimeSpan.Zero);
        var resets = since.AddHours(2).AddMinutes(51);
        var wait = new ResetWait(JobId.New(), new ConnectionName("claude-work"), "5h", resets, since) { AwaitsPerson = true };

        Assert.Equal(
            ($"window resets at {resets.ToLocalTime():HH:mm}", "window reset · waiting for you"),
            (FactPhrases.Of(new JobFact(FactKind.ResumesAtReset) { Wait = wait }), FactPhrases.Of(new JobFact(FactKind.ResumesAtReset) { Wait = wait with { HasReset = true } })));
        Assert.Equal(
            ($"The 5-hour window resets at {resets.ToLocalTime():HH:mm}, then it waits for you to resume", "The 5-hour window reset; send a message to resume, it reads its whole conversation again"),
            (Workbench.Inspector.InspectorPhrases.Waiting(wait), Workbench.Inspector.InspectorPhrases.Waiting(wait with { HasReset = true })));
    }

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
