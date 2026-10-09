using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Verification.Contracts;

namespace Avala.Verification.Tests.Verifying;

public sealed class ChecksGateTests
{
    private const string BuildAndTest = """
        { "checks": [
          { "name": "build", "command": "dotnet", "arguments": ["build"] },
          { "name": "tests", "command": "tester", "arguments": ["run", "all suites"], "timeoutSeconds": 60 }
        ] }
        """;

    private static readonly JobId Job = JobId.New();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ARepositoryWithoutDeclaredChecksPassesWithEvidenceThatNoneWereDeclaredAsync()
    {
        var verified = new Verified(Option<string>.None);

        var verdict = await verified.EvaluateAsync(Job, 1, Cancellation);

        Assert.Equal(GateVerdict.Pass, verdict);
        var report = Assert.Single(verified.Published);
        Assert.Equal((Job, 1, VerificationOutcome.NoChecksDeclared), (report.Job, report.Attempt, report.Outcome));
        Assert.Empty(report.Checks);
        Assert.Empty(verified.Processes.Requests);
        Assert.Equal([report], verified.Book.OfJob(Job));
    }

    [Fact]
    public async Task PassingChecksRunInTheWorktreeInOrderAndPassWithTheirEvidenceAsync()
    {
        var verified = new Verified(BuildAndTest);
        verified.Processes
            .Exits("dotnet", 0, TimeSpan.FromSeconds(12), "Build succeeded.\n")
            .Exits("tester", 0, TimeSpan.FromSeconds(3), "Passed: 12\n", "1 warning\n");

        var verdict = await verified.EvaluateAsync(Job, 2, Cancellation);

        Assert.Equal(GateVerdict.Pass, verdict);
        Assert.Equal(
            [("dotnet", "build"), ("tester", "run|all suites")],
            verified.Processes.Requests.Select(request => (request.FileName, string.Join('|', request.Arguments))));
        Assert.All(verified.Processes.Requests, request => Assert.Equal(Option<string>.Some(Verified.Worktree), request.WorkingDirectory));
        var report = Assert.Single(verified.Published);
        Assert.Equal((2, VerificationOutcome.Passed, verdict, verified.Clock.GetUtcNow()), (report.Attempt, report.Outcome, report.Verdict, report.VerifiedAt));
        Assert.Equal(
            [
                new CheckEvidence("build", "dotnet build", CheckStatus.Passed, 0, TimeSpan.FromSeconds(12), "Build succeeded.", string.Empty),
                new CheckEvidence("tests", "tester run \"all suites\"", CheckStatus.Passed, 0, TimeSpan.FromSeconds(3), "Passed: 12", "1 warning"),
            ],
            report.Checks);
    }

    [Fact]
    public async Task AFailingCheckSendsItsCommandExitCodeAndOutputBackAndSkipsTheRestAsync()
    {
        var verified = new Verified(BuildAndTest);
        verified.Processes
            .Exits("dotnet", 1, TimeSpan.FromSeconds(4), "Program.cs(3,1): error CS1002: ; expected\n", "Build FAILED.\n")
            .Exits("tester", 0, TimeSpan.FromSeconds(3));

        var verdict = await verified.EvaluateAsync(Job, 1, Cancellation);

        Assert.Equal(GateDecision.Retry, verdict.Decision);
        Assert.Equal(
            "The repository check \"build\" did not pass: `dotnet build` exited with code 1 after 4.0 s. Fix the cause, then finish the turn again."
            + "\n\nOutput:\nProgram.cs(3,1): error CS1002: ; expected\n\nErrors:\nBuild FAILED.",
            verdict.Feedback);
        Assert.Equal(["dotnet"], verified.Processes.Requests.Select(request => request.FileName));
        var report = Assert.Single(verified.Published);
        Assert.Equal((VerificationOutcome.Failed, verdict), (report.Outcome, report.Verdict));
        Assert.Equal(
            [(CheckStatus.Failed, Option<int>.Some(1)), (CheckStatus.Skipped, Option<int>.None)],
            report.Checks.Select(check => (check.Status, check.ExitCode)));
    }

    [Fact]
    public async Task ACheckThatOutlastsItsTimeoutIsStoppedAndReportedAsTimedOutAsync()
    {
        var verified = new Verified("""{ "checks": [{ "name": "tests", "command": "tester", "timeoutSeconds": 60 }] }""");
        verified.Processes.Hangs("tester", TimeSpan.FromSeconds(61));

        var verdict = await verified.EvaluateAsync(Job, 1, Cancellation);

        Assert.Equal(
            GateVerdict.Retry("The repository check \"tests\" did not pass: `tester` was stopped after 61.0 s, its time limit. Fix the cause, then finish the turn again."),
            verdict);
        var check = Assert.Single(Assert.Single(verified.Published).Checks);
        Assert.Equal((CheckStatus.TimedOut, Option<int>.None, TimeSpan.FromSeconds(61)), (check.Status, check.ExitCode, check.Duration));
    }

    [Fact]
    public async Task AJobFlowCancelledWhileACheckRunsIsNotReportedAsATimeoutAsync()
    {
        var verified = new Verified("""{ "checks": [{ "name": "tests", "command": "tester", "timeoutSeconds": 60 }] }""");
        verified.Processes.Hangs("tester", TimeSpan.FromSeconds(30));
        using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(10), verified.Clock);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await verified.EvaluateAsync(Job, 1, shutdown.Token));

        Assert.Empty(verified.Published);
    }

    [Fact]
    public async Task ACheckWhoseCommandIsMissingFailsAsNotFoundAsync()
    {
        var verified = new Verified("""{ "checks": [{ "name": "lint", "command": "no-such-linter" }] }""");

        var verdict = await verified.EvaluateAsync(Job, 1, Cancellation);

        Assert.Equal(
            GateVerdict.Retry("The repository check \"lint\" did not pass: `no-such-linter` could not start because its command was not found. Fix the cause, then finish the turn again."),
            verdict);
        Assert.Equal(CheckStatus.NotFound, Assert.Single(Assert.Single(verified.Published).Checks).Status);
    }

    [Fact]
    public async Task AnInvalidDeclarationIsSentBackWithoutRunningAnythingAsync()
    {
        var verified = new Verified("""{ "checks": [{ "arguments": ["build"] }] }""");

        var verdict = await verified.EvaluateAsync(Job, 1, Cancellation);

        Assert.Equal(
            GateVerdict.Retry("The check declaration in .avala/checks.json is invalid: a check has no command. Fix the file so its checks can run, then finish the turn again."),
            verdict);
        Assert.Empty(verified.Processes.Requests);
        var report = Assert.Single(verified.Published);
        Assert.Equal((VerificationOutcome.InvalidDeclaration, verdict), (report.Outcome, report.Verdict));
        Assert.Empty(report.Checks);
    }

    [Fact]
    public async Task TheEvidenceOfAJobListsItsAttemptsInOrderAndNoOtherJobAsync()
    {
        var verified = new Verified("""{ "checks": [{ "command": "dotnet", "arguments": ["build"] }] }""");
        var other = JobId.New();
        verified.Processes.Exits("dotnet", 1, TimeSpan.FromSeconds(1));
        await verified.EvaluateAsync(Job, 1, Cancellation);
        await verified.EvaluateAsync(other, 1, Cancellation);
        verified.Processes.Exits("dotnet", 0, TimeSpan.FromSeconds(1));
        await verified.EvaluateAsync(Job, 2, Cancellation);

        Assert.Equal(
            [(1, VerificationOutcome.Failed), (2, VerificationOutcome.Passed)],
            verified.Book.OfJob(Job).Select(report => (report.Attempt, report.Outcome)));
        Assert.Empty(verified.Book.OfJob(JobId.New()));
    }
}
