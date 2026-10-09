using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Verification.Contracts;
using Avala.Verification.Storage;
using Avala.Verification.Tests.Verifying;
using Avala.Workspaces.Contracts;

namespace Avala.Verification.Tests.Storage;

public sealed class EvidenceHistoryTests
{
    private static readonly DateTimeOffset Nine = new(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheReportsOfEarlierRunsCountForTheirJobBeforeThisRunsAndEveryNewReportIsStoredAsync()
    {
        var verified = new Verified("""{ "checks": [{ "command": "dotnet", "arguments": ["build"] }] }""");
        var job = JobId.New();
        var earlier = Failed(job, 1);
        verified.Store.Earlier = [earlier, Failed(JobId.New(), 1)];
        await verified.Book.RunAsync(Cancellation);
        verified.Processes.Exits("dotnet", 0, TimeSpan.FromSeconds(1));

        await verified.EvaluateAsync(job, 2, Cancellation);

        var stored = Assert.Single(verified.Store.Recorded);
        Assert.Equal([earlier, stored], verified.Book.OfJob(job));
    }

    [Fact]
    public async Task AReportSurvivesAReopeningWithEveryCheckAndEarlierRunsLeaveOutWhatThisRunStoredAsync()
    {
        await using var folder = new TemporaryFolder();
        var earlier = Failed(JobId.New(), 1);
        await using (var first = new SqliteEvidenceStore(new AvalaPaths(folder.Path)))
        {
            await first.RecordAsync(earlier, Cancellation);
        }

        await using var second = new SqliteEvidenceStore(new AvalaPaths(folder.Path));
        await second.RecordAsync(Failed(JobId.New(), 2), Cancellation);

        var read = Assert.Single(await second.EarlierRunsAsync(Cancellation));
        Assert.Equal(earlier.Checks, read.Checks);
        Assert.Equal(earlier with { Checks = read.Checks }, read);
    }

    private static VerificationReport Failed(JobId job, int attempt) =>
        new(
            job,
            attempt,
            VerificationOutcome.Failed,
            new FileOrigin("4f2a9c1", EditedInWorktree: true),
            [
                new CheckEvidence("build", "dotnet build", CheckStatus.Passed, 0, TimeSpan.FromSeconds(12.5), "Build succeeded.", string.Empty),
                new CheckEvidence("tests", "dotnet test", CheckStatus.Failed, 1, TimeSpan.FromSeconds(3), "[...]Failed: 1", "add(2, 2) = 5"),
                new CheckEvidence("lint", "dotnet format", CheckStatus.Skipped, Option<int>.None, TimeSpan.Zero, string.Empty, string.Empty),
            ],
            GateVerdict.Retry("The tests failed."),
            Nine.AddMinutes(attempt));
}
