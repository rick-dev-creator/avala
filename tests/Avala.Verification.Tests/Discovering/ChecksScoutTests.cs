using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Verification.Contracts;
using Avala.Verification.Discovering;

namespace Avala.Verification.Tests.Discovering;

public sealed class ChecksScoutTests
{
    private const string Proposal = """
        { "checks": [
          { "name": "lint", "command": "npm", "arguments": ["run", "lint"], "timeoutSeconds": 120 },
          { "name": "e2e", "command": "playwright", "arguments": ["test"] },
          { "name": "tests", "command": "npm", "arguments": ["test"], "timeoutSeconds": 300 }
        ] }
        """;

    private static CancellationToken Cancellation => Scouted.Cancellation;

    [Fact]
    public async Task AJobInARepositoryWithoutChecksStartsAHiddenAutonomousScoutOnTheResearchRoleAsync()
    {
        await using var scouted = Scouted.Fresh();

        var job = await scouted.RunningAsync();

        var (scout, request) = Assert.Single(scouted.Jobs.Submitted);
        Assert.Equal(
            (Scouted.Repository, ScoutBrief.Text, Option<JobId>.Some(job), true, Option<Autonomy>.Some(Autonomy.Autonomous), Scouted.FixedRoles.Research),
            (request.RepositoryPath, request.Instruction, request.Parent, request.Companion, request.Autonomy, request.Model));
        var discovery = Assert.Single(scouted.Store.Kept);
        Assert.Equal((job, DiscoveryState.Running, Option<JobId>.Some(scout)), (discovery.Job, discovery.State, discovery.Scout));
        var changed = Assert.Single(scouted.Bus.Published.OfType<ChecksDiscoveryChanged>()).Discovery;
        Assert.Equal((job, Scouted.Repository, DiscoveryState.Running), (changed.Job, changed.Repository, changed.State));
        Assert.Empty(changed.Checks);
    }

    [Fact]
    public void TheScoutIsToldToRunTheCommandsOfAnyStackAndWriteOnlyItsProposal()
    {
        Assert.Contains("whatever its languages and tools are", ScoutBrief.Text, StringComparison.Ordinal);
        Assert.Contains("Run each command here", ScoutBrief.Text, StringComparison.Ordinal);
        Assert.Contains($"write the commands you kept to `{ChecksProposal.Path}`", ScoutBrief.Text, StringComparison.Ordinal);
        Assert.Contains("Do not write `.avala/checks.json` yourself", ScoutBrief.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet", ScoutBrief.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AJobWhoseCommitDeclaresChecksStartsNoScoutAsync()
    {
        await using var scouted = Scouted.Fresh();

        _ = await scouted.RunningAsync("""{ "checks": [] }""");

        Assert.Empty(scouted.Jobs.Submitted);
        Assert.Empty(scouted.Store.Kept);
    }

    [Theory]
    [InlineData("child")]
    [InlineData("companion")]
    [InlineData("scratch")]
    public async Task OnlyAJobAPersonStartedInTheirOwnRepositoryIsScoutedAsync(string kind)
    {
        await using var scouted = Scouted.Fresh();
        var parent = scouted.Job();
        var job = kind switch
        {
            "child" => scouted.Job(parent: parent),
            "companion" => scouted.Job(parent: parent, companion: true),
            _ => scouted.Job(repository: $"{Scouted.Scratch.Folder}/2026-10-24-sketch-a-parser-3f9a1c"),
        };

        await scouted.Scout.HandleAsync(new JobProgressed(job, JobStatus.Running), Cancellation);

        Assert.Empty(scouted.Jobs.Submitted);
    }

    [Fact]
    public async Task TurningDiscoveryOffStartsNoScoutAsync()
    {
        await using var scouted = Scouted.Fresh();
        scouted.Settings.Discovers = false;

        _ = await scouted.RunningAsync();

        Assert.Empty(scouted.Jobs.Submitted);
    }

    [Fact]
    public async Task AJobThatRunsAgainOrIsJudgedWhileItsScoutWorksStartsOneScoutAsync()
    {
        await using var scouted = Scouted.Fresh();
        var job = await scouted.RunningAsync();

        await scouted.MoveAsync(job, JobStatus.Running);
        await Task.WhenAll(scouted.Desk.StartAsync(job, Cancellation), scouted.Desk.StartAsync(job, Cancellation));

        Assert.Single(scouted.Jobs.Submitted);
    }

    [Fact]
    public async Task TheProposedCommandsThatPassAgainBecomeTheJobsChecksShippedInItsWorktreeAsync()
    {
        await using var scouted = Scouted.Fresh();
        var job = await scouted.RunningAsync();
        var scout = scouted.LastScout;
        scouted.Proposes(scout, Proposal);
        scouted.Processes.Exits("npm", 0, TimeSpan.FromSeconds(20)).Exits("playwright", 1, TimeSpan.FromSeconds(5));

        await scouted.MoveAsync(scout, JobStatus.AwaitingReview);

        const string Declaration = """
            {
              "checks": [
                {
                  "name": "lint",
                  "command": "npm",
                  "arguments": [
                    "run",
                    "lint"
                  ],
                  "timeoutSeconds": 120
                },
                {
                  "name": "tests",
                  "command": "npm",
                  "arguments": [
                    "test"
                  ],
                  "timeoutSeconds": 300
                }
              ]
            }

            """;
        Assert.Equal(Declaration.ReplaceLineEndings("\n"), scouted.Folders[Scouted.FakeJobs.Worktree(job), Scouted.ChecksPath]);
        Assert.All(scouted.Processes.Requests, request => Assert.Equal(Option<string>.Some(Scouted.FakeJobs.Worktree(scout)), request.WorkingDirectory));
        var discovery = Assert.Single(scouted.Store.Kept);
        Assert.Equal((DiscoveryState.Found, Declaration.ReplaceLineEndings("\n")), (discovery.State, discovery.Declaration));
        Assert.Equal(["lint", "tests"], scouted.Book.OfJob(job).Match(found => found.Checks, () => []).Select(check => check.Name));
        Assert.Equal([scout], scouted.Jobs.Discarded);
    }

    [Fact]
    public async Task ADeclarationTheAgentAlreadyWroteInTheWorktreeIsNotOverwrittenAsync()
    {
        await using var scouted = Scouted.Fresh();
        var job = await scouted.RunningAsync();
        scouted.Folders.Draft(Scouted.FakeJobs.Worktree(job), Scouted.ChecksPath, """{ "checks": [] }""");
        scouted.Proposes(scouted.LastScout, Proposal);
        scouted.Processes.Exits("npm", 0, TimeSpan.FromSeconds(20));

        await scouted.MoveAsync(scouted.LastScout, JobStatus.AwaitingReview);

        Assert.Equal("""{ "checks": [] }""", scouted.Folders[Scouted.FakeJobs.Worktree(job), Scouted.ChecksPath]);
        Assert.Equal(DiscoveryState.Found, Assert.Single(scouted.Store.Kept).State);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{ "checks": [] }""")]
    [InlineData("""{ "checks": [{ "name": "e2e", "command": "playwright" }] }""")]
    public async Task AProposalWithNothingThatPassesFindsNoChecksAndShipsNothingAsync(string proposal)
    {
        await using var scouted = Scouted.Fresh();
        var job = await scouted.RunningAsync();

        if (proposal.Length > 0)
        {
            scouted.Proposes(scouted.LastScout, proposal);
        }

        scouted.Processes.Exits("playwright", 1, TimeSpan.FromSeconds(5));

        await scouted.MoveAsync(scouted.LastScout, JobStatus.AwaitingReview);

        Assert.Equal(DiscoveryState.NoneFound, Assert.Single(scouted.Store.Kept).State);
        Assert.Null(scouted.Folders[Scouted.FakeJobs.Worktree(job), Scouted.ChecksPath]);
        Assert.Equal([scouted.LastScout], scouted.Jobs.Discarded);
    }

    [Fact]
    public async Task AProposalIsCutToEightChecksOfThirtyMinutesAtMostAsync()
    {
        await using var scouted = Scouted.Fresh();
        _ = await scouted.RunningAsync();
        var checks = string.Join(", ", Enumerable.Range(1, 10).Select(number => $$"""{ "name": "step {{number}}", "command": "make", "timeoutSeconds": 86400 }"""));
        scouted.Proposes(scouted.LastScout, $$"""{ "checks": [{{checks}}] }""");
        scouted.Processes.Exits("make", 0, TimeSpan.FromSeconds(1));

        await scouted.MoveAsync(scouted.LastScout, JobStatus.AwaitingReview);

        var found = Assert.Single(scouted.Store.Kept).Checks;
        Assert.Equal(ProposalReview.MostChecks, found.Count);
        Assert.All(found, check => Assert.Equal(TimeSpan.FromMinutes(30), check.Timeout));
    }

    [Theory]
    [InlineData(JobStatus.NeedsHelp, true)]
    [InlineData(JobStatus.Failed, false)]
    [InlineData(JobStatus.Discarded, false)]
    public async Task AScoutThatIsHeldOrEndsFindsNoChecksAndIsDiscardedIfStillOpenAsync(JobStatus status, bool discarded)
    {
        await using var scouted = Scouted.Fresh();
        _ = await scouted.RunningAsync();
        var scout = scouted.LastScout;

        await scouted.MoveAsync(scout, status);

        Assert.Equal(DiscoveryState.NoneFound, Assert.Single(scouted.Store.Kept).State);
        Assert.Equal(discarded ? [scout] : [], scouted.Jobs.Discarded);
    }

    [Theory]
    [InlineData(JobStatus.Approved)]
    [InlineData(JobStatus.Discarded)]
    [InlineData(JobStatus.Failed)]
    public async Task AJobThatEndsWhileItsScoutWorksStopsTheScoutAsync(JobStatus status)
    {
        await using var scouted = Scouted.Fresh();
        var job = await scouted.RunningAsync();

        await scouted.MoveAsync(job, status);

        Assert.Equal(DiscoveryState.NoneFound, Assert.Single(scouted.Store.Kept).State);
        Assert.Equal([scouted.LastScout], scouted.Jobs.Discarded);
    }

    [Fact]
    public async Task ALaterJobInTheSameRepositoryShipsTheChecksAlreadyFoundWithoutAScoutAsync()
    {
        await using var scouted = Scouted.Fresh();
        _ = await scouted.RunningAsync();
        scouted.Proposes(scouted.LastScout, Proposal);
        scouted.Processes.Exits("npm", 0, TimeSpan.FromSeconds(20));
        await scouted.MoveAsync(scouted.LastScout, JobStatus.AwaitingReview);
        var found = Assert.Single(scouted.Store.Kept).Declaration;

        var later = await scouted.RunningAsync();
        var elsewhere = await scouted.RunningAsync(repository: "/repos/blog");

        Assert.Equal(2, scouted.Jobs.Submitted.Count);
        Assert.Equal(Option<JobId>.Some(elsewhere), scouted.Jobs.Submitted[^1].Request.Parent);
        Assert.Equal(found, scouted.Folders[Scouted.FakeJobs.Worktree(later), Scouted.ChecksPath]);
        Assert.Equal((DiscoveryState.Found, Option<JobId>.None), scouted.Book.Of(later).Match(discovery => (discovery.State, discovery.Scout), () => default));
    }

    [Fact]
    public async Task AScoutThatFinishesBeforeItIsRecordedIsStillConcludedAsync()
    {
        await using var scouted = Scouted.Fresh();
        scouted.Jobs.SubmittedAs = JobStatus.AwaitingReview;
        var job = scouted.Job();

        await scouted.Desk.StartAsync(job, Cancellation);

        Assert.Equal(DiscoveryState.NoneFound, Assert.Single(scouted.Store.Kept).State);
        Assert.Equal([scouted.LastScout], scouted.Jobs.Discarded);
    }

    [Fact]
    public async Task ARefusedScoutFindsNoChecksAsync()
    {
        await using var scouted = Scouted.Fresh();
        scouted.Jobs.Rejection = JobRejection.ParentNotRunning;

        _ = await scouted.RunningAsync();

        Assert.Equal((DiscoveryState.NoneFound, Option<JobId>.None), Assert.Single(scouted.Store.Kept) is var kept ? (kept.State, kept.Scout) : default);
    }

    [Fact]
    public async Task AfterARestartAScoutThatFinishedWhileAvalaWasClosedIsConcludedAsync()
    {
        var scouted = Scouted.Fresh();
        var job = await scouted.RunningAsync();
        var scout = scouted.LastScout;
        scouted.Jobs.Move(scout, JobStatus.AwaitingReview);
        scouted.Proposes(scout, Proposal);
        scouted.Processes.Exits("npm", 0, TimeSpan.FromSeconds(20));

        await using var restarted = await scouted.RestartedAsync();

        Assert.Equal(DiscoveryState.Found, restarted.Book.Of(job).Match(discovery => discovery.State, () => DiscoveryState.Running));
        Assert.Equal([scout], restarted.Jobs.Discarded);
    }

    [Fact]
    public async Task AfterARestartTheScoutOfAJobThatEndedIsStoppedAsync()
    {
        var scouted = Scouted.Fresh();
        var job = await scouted.RunningAsync();
        scouted.Jobs.Move(job, JobStatus.Discarded);

        await using var restarted = await scouted.RestartedAsync();

        Assert.Equal(DiscoveryState.NoneFound, restarted.Book.Of(job).Match(discovery => discovery.State, () => DiscoveryState.Running));
        Assert.Equal([scouted.LastScout], restarted.Jobs.Discarded);
    }
}
