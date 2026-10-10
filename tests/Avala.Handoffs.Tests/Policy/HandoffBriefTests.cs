using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Handoffs.Contracts;
using Avala.Handoffs.Policy;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Verification.Contracts;
using Avala.Workspaces.Contracts;

namespace Avala.Handoffs.Tests.Policy;

public sealed class HandoffBriefTests
{
    private static readonly ConnectionName Work = new("claude-work");

    private static readonly ConnectionName Personal = new("claude-personal");

    [Fact]
    public void TheBriefSaysWhereTheJobCameFromAndWhyAndHoldsEverythingAvalaRecorded()
    {
        var report = new VerificationReport(
            JobId.New(),
            1,
            VerificationOutcome.Failed,
            Option<FileOrigin>.None,
            [
                new CheckEvidence("build", "dotnet build", CheckStatus.Passed, 0, TimeSpan.FromSeconds(3), string.Empty, string.Empty),
                new CheckEvidence("tests", "dotnet test", CheckStatus.Failed, 1, TimeSpan.FromSeconds(9), string.Empty, string.Empty),
            ],
            new GateVerdict(GateDecision.Retry, "tests failed"),
            DateTimeOffset.UnixEpoch);

        var brief = HandoffBrief.Compose(new BriefFacts("Fix JPY rounding in invoice totals", Work, Personal, new LimitReason(Work, "5h", 0.91, 0.9))
        {
            Plan = [new PlanStep("Write the change", PlanStepStatus.Done), new PlanStep("Run the tests", PlanStepStatus.InProgress)],
            Files = Option<IReadOnlyList<FileChange>>.Some([new FileChange("src/Money.cs", ChangeKind.Modified, 12, 3)]),
            Verification = report,
            Feedback = "The checks did not pass. Review the failures and fix them.",
            Decisions = ["Allowed Command dotnet test by the rule tests"],
            Questions = ["Choose a database: The service needs to store its orders."],
            LastMessage = "Rounded the totals; the tests still fail on JPY.",
        });

        Assert.StartsWith("This job was handed off to you from claude-work, which reached 91% of its 5-hour window. You continue it on claude-personal in a new conversation.", brief, StringComparison.Ordinal);
        Assert.All(
            [
                "## The original instruction\nFix JPY rounding in invoice totals",
                "- [done] Write the change\n- [in progress] Run the tests",
                "- src/Money.cs (modified, +12 -3)",
                "The last verification, of attempt 1: failed.\n- build: Passed, exit 0\n- tests: Failed, exit 1\nFeedback: The checks did not pass.",
                "- Allowed Command dotnet test by the rule tests",
                "- Choose a database: The service needs to store its orders.",
                "Rounded the totals; the tests still fail on JPY.",
                "- Make the failing checks pass: tests.\n- Finish the pending steps of the plan: Run the tests.",
            ],
            expected => Assert.Contains(expected.ReplaceLineEndings(), brief, StringComparison.Ordinal));
    }

    [Fact]
    public void ASectionWithoutRecordsSaysSo()
    {
        var brief = HandoffBrief.Compose(new BriefFacts("Add a greeting", Work, Personal, Option<LimitReason>.None));

        Assert.All(
            ["No plan was reported.", "The changes could not be read.", "No checks ran yet.", "## Decisions taken\nNone.", "- Complete the original instruction."],
            expected => Assert.Contains(expected.ReplaceLineEndings(), brief, StringComparison.Ordinal));
        Assert.StartsWith("This job was handed off to you from claude-work. ", brief, StringComparison.Ordinal);
    }

    [Fact]
    public void ListsKeepTheirLatestEntriesAndTheLastMessageIsBounded()
    {
        var brief = HandoffBrief.Compose(new BriefFacts("Add a greeting", Work, Personal, Option<LimitReason>.None)
        {
            Decisions = [.. Enumerable.Range(1, 25).Select(number => $"Allowed decision {number}")],
            LastMessage = new string('x', HandoffBrief.LongestMessage + 50),
        });

        Assert.Contains("(5 earlier left out)", brief, StringComparison.Ordinal);
        Assert.DoesNotContain("Allowed decision 5\n".ReplaceLineEndings(), brief, StringComparison.Ordinal);
        Assert.Contains("Allowed decision 25", brief, StringComparison.Ordinal);
        Assert.Contains($"{new string('x', HandoffBrief.LongestMessage)}[...]", brief, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWholeBriefIsBounded() =>
        Assert.Equal(
            HandoffBrief.LongestBrief,
            HandoffBrief.Compose(new BriefFacts(new string('i', HandoffBrief.LongestBrief), Work, Personal, Option<LimitReason>.None)).Length);
}
