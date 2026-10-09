using System.Text.Json;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Budgets.Contracts;
using Avala.Delegation.Contracts;
using Avala.Delegation.Policy;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Verification.Contracts;
using Avala.Workspaces.Contracts;

namespace Avala.Delegation.Tests.Delegating;

public sealed class DelegationDeskTests
{
    [Fact]
    public async Task ACallSubmitsAChildOfTheCallersJobOnTheRoutedConnectionWithTheCallersAutonomyAsync()
    {
        await using var desk = new Desk();
        await desk.StartedAsync(Autonomy.Autonomous);

        var first = await desk.DelegateAsync("first");
        var second = await desk.DelegateAsync("second", "Write the to-do list");

        Assert.Equal(
            [
                (FakeJobs.Repository, "Write the release notes", Option<JobId>.Some(desk.Parent), Option<ConnectionName>.Some(new ConnectionName("work")), Option<Autonomy>.Some(Autonomy.Autonomous)),
                (FakeJobs.Repository, "Write the to-do list", Option<JobId>.Some(desk.Parent), Option<ConnectionName>.Some(new ConnectionName("personal")), Option<Autonomy>.Some(Autonomy.Autonomous)),
            ],
            desk.Jobs.Submitted.Select(request => (request.RepositoryPath, request.Instruction, request.Parent, request.Connection, request.Autonomy)));
        var delegated = desk.Bus.Published.OfType<ChildDelegated>().Select(announced => announced.Delegation).ToList();
        Assert.Equal([Option<JobId>.Some(first), Option<JobId>.Some(second)], delegated.Select(record => record.Child));
        Assert.All(delegated, record => Assert.Equal((Option<JobId>.Some(desk.Parent), 1), (record.Parent, record.Depth)));
        Assert.Empty(desk.Agents.Results);
    }

    [Fact]
    public async Task AVerifiedChildIsIntegratedAndItsEvidenceReturnsToTheParentAsItsResultAsync()
    {
        await using var desk = new Desk();
        await desk.StartedAsync();
        var child = await desk.DelegateAsync("notes");
        desk.Changes.Files = [new FileChange("NOTES.md", ChangeKind.Added, 3, 0)];
        desk.Verifications.Reports[child] = Verified(child);
        desk.Usage.Jobs[child] = new UsageSummary(new TokenUsage(1_000, 200, 0, 0, 0), [new Cost(0.05m, "USD")], 0, default, []);
        desk.Budgets.Carves[child] = new BudgetCarve(desk.Parent, child, [new Cost(0.50m, "USD")], 500L, 0.5, desk.Clock.GetUtcNow());
        await desk.ReplyAsync(child, "Wrote ", "NOTES.md.");

        await desk.ProgressAsync(child, JobStatus.AwaitingReview);
        var reported = await desk.ReportedAsync(child);

        var report = Outcomes.Present(reported.Report);
        Assert.Equal((ChildOutcome.Integrated, JobStatus.Approved, Option<string>.Some("Wrote NOTES.md.")), (report.Outcome, report.Status, report.Summary));
        Assert.Equal((1_200L, 0.05m, 0.50m), (report.Tokens, report.Spent.Single().Amount, Outcomes.Present(report.Carve).Cost.Single().Amount));
        var (session, result) = Assert.Single(desk.Agents.Results);
        Assert.Equal((desk.Session, new ItemId("notes"), false), (session, result.Item, result.IsError));
        using var answer = JsonDocument.Parse(result.Content);
        var root = answer.RootElement;
        Assert.Equal(
            ("integrated", "work", "Wrote NOTES.md.", "NOTES.md", "passed", "c0ffee"),
            (root.GetProperty("outcome").GetString(), root.GetProperty("connection").GetString(), root.GetProperty("summary").GetString(),
                root.GetProperty("files")[0].GetProperty("path").GetString(), root.GetProperty("verification").GetProperty("outcome").GetString(),
                root.GetProperty("integrated").GetProperty("commit").GetString()));
        Assert.Equal(reported, Outcomes.Present(desk.Book.OfChild(child)));
        Assert.Equal([reported], desk.Book.OfParent(desk.Parent));
    }

    [Fact]
    public async Task AChildWhoseWorkConflictsWithItsParentIsReportedAsAConflictWithTheConflictingFilesAsync()
    {
        await using var desk = new Desk();
        await desk.StartedAsync();
        var child = await desk.DelegateAsync("notes");
        desk.Jobs.Approval = JobRejection.MergeConflict;
        desk.Changes.Conflicting = ["NOTES.md"];

        await desk.ProgressAsync(child, JobStatus.AwaitingReview);

        var report = Outcomes.Present((await desk.ReportedAsync(child)).Report);
        Assert.Equal(
            (ChildOutcome.Conflict, JobStatus.AwaitingReview, Option<JobRejection>.Some(JobRejection.MergeConflict), "NOTES.md"),
            (report.Outcome, report.Status, report.Refusal, Assert.Single(report.Conflicts)));
        Assert.False(Assert.Single(desk.Agents.Results).Result.IsError);
    }

    [Theory]
    [InlineData("held", "Held")]
    [InlineData("out of retries", "RetriesExhausted")]
    [InlineData("failed", "Failed")]
    public async Task AChildThatNeedsHelpOrFailedIsReportedWithoutBeingIntegratedAsync(string situation, string expected)
    {
        await using var desk = new Desk();
        await desk.StartedAsync();
        var child = await desk.DelegateAsync("notes");
        desk.Jobs.Attempted(child, situation == "held" ? AttemptOutcome.Interrupted : AttemptOutcome.Rejected);

        await desk.ProgressAsync(child, situation == "failed" ? JobStatus.Failed : JobStatus.NeedsHelp);
        await desk.HeldAsync(child, HoldReason.BudgetExceeded);

        var report = Outcomes.Present((await desk.ReportedAsync(child)).Report);
        Assert.Equal(Enum.Parse<ChildOutcome>(expected), report.Outcome);
        Assert.Equal(situation == "held" ? Option<HoldReason>.Some(HoldReason.BudgetExceeded) : Option<HoldReason>.None, report.Hold);
        Assert.Single(desk.Agents.Results);
    }

    [Theory]
    [InlineData("not declared", "NotDeclared")]
    [InlineData("unreadable rules", "Malformed")]
    [InlineData("too deep", "DepthExceeded")]
    [InlineData("too many", "TooManyChildren")]
    [InlineData("looser", "AutonomyLoosened")]
    [InlineData("malformed", "MalformedInput")]
    [InlineData("no job", "NoJob")]
    [InlineData("not submitted", "NotSubmitted")]
    public async Task ACallThePolicyRefusesIsAnsweredWithATypedErrorAndSubmitsNothingAsync(string situation, string expected)
    {
        await using var desk = new Desk(situation switch
        {
            "not declared" => Option<DelegationRules>.None,
            "unreadable rules" => DelegationError.Malformed,
            _ => Option<DelegationRules>.Some(Desk.Declared with { MaxChildren = 1 }),
        });
        var caller = situation == "too deep" ? desk.Jobs.Running(parent: desk.Parent) : desk.Parent;
        await (situation == "no job" ? Task.CompletedTask : desk.StartedAsync(caller));
        await (situation == "too many" ? desk.DelegateAsync("first") : Task.CompletedTask);
        desk.Jobs.Rejection = situation == "not submitted" ? JobRejection.UnknownConnection : Option<JobRejection>.None;
        var submitted = desk.Jobs.Submitted.Count;

        await desk.CallAsync("call", situation switch
        {
            "looser" => """{ "instruction": "Write the notes", "autonomy": "autonomous" }""",
            "malformed" => """{ "instruction": "Write the notes", "connection": "personal" }""",
            _ => """{ "instruction": "Write the notes" }""",
        });

        var refused = Assert.Single(desk.Bus.Published.OfType<DelegationRefused>()).Delegation;
        Assert.Equal(Option<DelegationError>.Some(Enum.Parse<DelegationError>(expected)), refused.Refusal);
        Assert.Equal(situation == "not submitted" ? submitted + 1 : submitted, desk.Jobs.Submitted.Count);
        var (_, result) = desk.Agents.Results[^1];
        Assert.Equal((new ItemId("call"), true), (result.Item, result.IsError));
        using var answer = JsonDocument.Parse(result.Content);
        Assert.Equal($"{char.ToLowerInvariant(expected[0])}{expected[1..]}", answer.RootElement.GetProperty("refused").GetString());
    }

    [Fact]
    public async Task TheChildrenStillRunningWhenTheirParentEndsAreDiscardedAsync()
    {
        await using var desk = new Desk();
        await desk.StartedAsync();
        var running = await desk.DelegateAsync("running");
        var done = await desk.DelegateAsync("done");
        await desk.ProgressAsync(done, JobStatus.Failed);
        _ = await desk.ReportedAsync(done);

        await desk.ProgressAsync(desk.Parent, JobStatus.Discarded);
        await desk.ProgressAsync(running, JobStatus.Discarded);

        Assert.Equal(ChildOutcome.Discarded, Outcomes.Present((await desk.ReportedAsync(running)).Report).Outcome);
        Assert.Equal([running], desk.Jobs.Discarded);
    }

    private static VerificationReport Verified(JobId child) =>
        new(child, 1, VerificationOutcome.Passed, Option<FileOrigin>.None, [new CheckEvidence("tests", "dotnet test", CheckStatus.Passed, 0, TimeSpan.FromSeconds(2), "Passed", string.Empty)], GateVerdict.Pass, DateTimeOffset.UnixEpoch);
}
