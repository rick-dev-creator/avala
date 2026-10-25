using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Verification.Contracts;
using Avala.Verification.Discovering;
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
    public async Task AReportSurvivesAReopeningWithEveryCheckItsProblemAndItsHoldAndEarlierRunsLeaveOutWhatThisRunStoredAsync()
    {
        await using var folder = new TemporaryFolder();
        var earlier = Failed(JobId.New(), 1);
        var invalid = Invalid(JobId.New(), 1);
        await using (var first = new SqliteEvidenceStore(new AvalaPaths(folder.Path)))
        {
            await first.RecordAsync(earlier, Cancellation);
            await first.RecordAsync(invalid, Cancellation);
        }

        await using var second = new SqliteEvidenceStore(new AvalaPaths(folder.Path));
        await second.RecordAsync(Failed(JobId.New(), 2), Cancellation);

        var read = await second.EarlierRunsAsync(Cancellation);
        Assert.Equal(2, read.Count);
        Assert.Equal(earlier.Checks, read[0].Checks);
        Assert.Equal(earlier with { Checks = read[0].Checks }, read[0]);
        Assert.Equal(invalid with { Checks = read[1].Checks }, read[1]);
    }

    [Fact]
    public async Task ADiscoverySurvivesAReopeningAndKeepingItAgainReplacesItAsync()
    {
        await using var folder = new TemporaryFolder();
        var running = new Discovery(JobId.New(), "/repos/shop", DiscoveryState.Running, Nine) { Scout = JobId.New() };
        var found = running with { State = DiscoveryState.Found, Declaration = """{ "checks": [{ "name": "tests", "command": "npm", "arguments": ["test"] }] }""", At = Nine.AddMinutes(4) };
        var none = new Discovery(JobId.New(), "/repos/blog", DiscoveryState.NoneFound, Nine);
        await using (var first = new SqliteEvidenceStore(new AvalaPaths(folder.Path)))
        {
            await first.KeepAsync(running, Cancellation);
            await first.KeepAsync(none, Cancellation);
            await first.KeepAsync(found, Cancellation);
        }

        await using var second = new SqliteEvidenceStore(new AvalaPaths(folder.Path));

        Assert.Equal([found, none], (await second.DiscoveriesAsync(Cancellation)).OrderBy(discovery => discovery.Repository, StringComparer.Ordinal).Reverse());
    }

    [Fact]
    public void AReportStoredBeforeAMissingChecksFileWasToldApartFromAnEmptyOneStillSaysTheFileIsMissing()
    {
        var undeclared = new VerificationReport(JobId.New(), 1, VerificationOutcome.NoChecksDeclared, Option<FileOrigin>.None, [], GateVerdict.Pass, Nine);
        var json = System.Text.Json.Nodes.JsonNode.Parse(StoredReport.Of(undeclared with { Missing = true }).Report)!.AsObject();
        _ = json.Remove(nameof(VerificationReport.Missing));

        Assert.True(new StoredReport { Report = json.ToJsonString() }.Read().Missing);
    }

    private static VerificationReport Invalid(JobId job, int attempt) =>
        new(
            job,
            attempt,
            VerificationOutcome.InvalidDeclaration,
            new FileOrigin("4f2a9c1", EditedInWorktree: false),
            [],
            GateVerdict.HoldFor(HoldReason.InvalidChecks),
            Nine.AddMinutes(attempt))
        {
            Problem = DeclarationProblem.MissingCommand,
        };

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
