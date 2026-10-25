using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
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

    private const string Declaration = """
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

    private static CancellationToken Cancellation => Scouted.Cancellation;

    private static Permissions.Contracts.PolicyDecision Asked(SessionId session, JobId job) =>
        new(session, TurnId.New(), new ItemId("install"), job, ItemKind.Command, "npm ci", Permissions.Contracts.PolicyAnswer.Ask, Option<Permissions.Contracts.PolicyRule>.None, Permissions.Contracts.DecisionDelivery.LeftToHuman, DateTimeOffset.UnixEpoch);

    private static string Found => Declaration.ReplaceLineEndings("\n");

    [Fact]
    public async Task AJobInARepositoryWithoutChecksStartsAHiddenScoutOnTheResearchRoleAsync()
    {
        await using var scouted = Scouted.Fresh();

        var job = await scouted.RunningAsync();

        var (scout, request) = Assert.Single(scouted.Jobs.Submitted);
        Assert.Equal(
            (Scouted.Repository, ScoutBrief.Text(runs: true), Option<JobId>.Some(job), true, Option<Autonomy>.Some(Autonomy.Autonomous), Scouted.FixedRoles.Research),
            (request.RepositoryPath, request.Instruction, request.Parent, request.Companion, request.Autonomy, request.Model));
        var discovery = Assert.Single(scouted.Store.Kept);
        Assert.Equal((job, DiscoveryState.Running, Option<JobId>.Some(scout), false), (discovery.Job, discovery.State, discovery.Scout, discovery.Reads));
        var changed = Assert.Single(scouted.Bus.Published.OfType<ChecksDiscoveryChanged>()).Discovery;
        Assert.Equal((job, Scouted.Repository, DiscoveryState.Running), (changed.Job, changed.Repository, changed.State));
    }

    [Fact]
    public void TheScoutIsToldToFindTheCommandsOfAnyStackAndProposeThemThroughItsTool()
    {
        foreach (var brief in new[] { ScoutBrief.Text(runs: true), ScoutBrief.Text(runs: false) })
        {
            Assert.StartsWith(ScoutBrief.Opening, brief, StringComparison.Ordinal);
            Assert.Contains("whatever its languages and tools are", brief, StringComparison.Ordinal);
            Assert.Contains($"call the `{ChecksProposal.Tool}` tool once", brief, StringComparison.Ordinal);
            Assert.Contains("make the command that installs them", brief, StringComparison.Ordinal);
            Assert.DoesNotContain("dotnet", brief, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("Run each command here", ScoutBrief.Text(runs: true), StringComparison.Ordinal);
        Assert.Contains("Do not run any command", ScoutBrief.Text(runs: false), StringComparison.Ordinal);
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
    public async Task JobsThatStartTogetherInOneRepositoryShareOneScoutAndItsChecksAsync()
    {
        await using var scouted = Scouted.Fresh();
        var first = await scouted.RunningAsync();
        var second = await scouted.RunningAsync();
        await scouted.ProposesAsync(scouted.LastScout, Proposal);
        scouted.Processes.Exits("npm", 0, TimeSpan.FromSeconds(20)).Exits("playwright", 1, TimeSpan.FromSeconds(5));

        await scouted.MoveAsync(scouted.LastScout, JobStatus.AwaitingReview);

        Assert.Single(scouted.Jobs.Submitted);
        Assert.Equal((DiscoveryState.Found, Found), scouted.Book.Of(second).Match(found => (found.State, found.Declaration), () => default));
        Assert.Equal(Found, scouted.Folders[Scouted.FakeJobs.Worktree(second), Scouted.ChecksPath]);
        Assert.Equal([first, second], scouted.Rechecked);
    }

    [Fact]
    public async Task WhenTheJobThatScoutsEndsAJobThatFollowedItGetsAScoutOfItsOwnAsync()
    {
        await using var scouted = Scouted.Fresh();
        var first = await scouted.RunningAsync();
        scouted.Clock.Advance(TimeSpan.FromSeconds(1));
        var second = await scouted.RunningAsync();
        scouted.Clock.Advance(TimeSpan.FromSeconds(1));
        var third = await scouted.RunningAsync();
        var abandoned = scouted.LastScout;

        await scouted.MoveAsync(first, JobStatus.Discarded);

        var (relaunched, request) = scouted.Jobs.Submitted[^1];
        Assert.Equal((2, Option<JobId>.Some(second)), (scouted.Jobs.Submitted.Count, request.Parent));
        Assert.Equal([abandoned], scouted.Jobs.Discarded);
        Assert.Equal((Option<JobId>.Some(relaunched), Option<JobId>.None), scouted.Book.Of(second).Match(found => (found.Scout, found.Follows), () => default));
        Assert.Equal(Option<JobId>.Some(second), scouted.Book.Of(third).Bind(found => found.Follows));
    }

    [Fact]
    public async Task TheProposedCommandsThatPassAgainBecomeTheJobsChecksShippedInItsWorktreeAsync()
    {
        await using var scouted = Scouted.Fresh();
        var job = await scouted.RunningAsync();
        var scout = scouted.LastScout;
        var received = await scouted.ProposesAsync(scout, Proposal);
        scouted.Processes.Exits("npm", 0, TimeSpan.FromSeconds(20)).Exits("playwright", 1, TimeSpan.FromSeconds(5));

        await scouted.MoveAsync(scout, JobStatus.AwaitingReview);

        Assert.Equal(("Avala received 3 checks. Finish your turn now.", false), (received.Content, received.IsError));
        Assert.Equal(Found, scouted.Folders[Scouted.FakeJobs.Worktree(job), Scouted.ChecksPath]);
        Assert.All(scouted.Processes.Requests, request => Assert.Equal(Option<string>.Some(Scouted.FakeJobs.Worktree(scout)), request.WorkingDirectory));
        Assert.Equal((DiscoveryState.Found, Found), scouted.Book.Of(job).Match(found => (found.State, found.Declaration), () => default));
        Assert.Equal([scout], scouted.Jobs.Discarded);
        Assert.Equal([job], scouted.Rechecked);
    }

    [Fact]
    public async Task InASupervisedRepositoryTheScoutOnlyReadsAndItsProposalWaitsForThePersonAsync()
    {
        await using var scouted = Scouted.Fresh();
        scouted.Policies.Autonomy = Autonomy.Supervised;
        var job = await scouted.RunningAsync();
        var scout = scouted.LastScout;
        await scouted.ProposesAsync(scout, Proposal);

        await scouted.MoveAsync(scout, JobStatus.AwaitingReview);

        Assert.Equal(ScoutBrief.Text(runs: false), scouted.Jobs.Submitted[^1].Request.Instruction);
        Assert.Empty(scouted.Processes.Requests);
        Assert.Empty(scouted.Jobs.Discarded);
        Assert.Equal(
            (DiscoveryState.Proposed, "lint,e2e,tests"),
            scouted.Book.OfJob(job).Match(found => (found.State, string.Join(',', found.Checks.Select(check => check.Name))), () => default));
        Assert.Equal(GateVerdict.Deferred, await scouted.EvaluateAsync(job));
        Assert.Empty(scouted.Reports);
    }

    [Fact]
    public async Task UsingTheProposedChecksRunsThemAndShipsTheOnesThatPassAsync()
    {
        await using var scouted = Scouted.Fresh();
        scouted.Policies.Autonomy = Autonomy.Supervised;
        var job = await scouted.RunningAsync();
        var scout = scouted.LastScout;
        await scouted.ProposesAsync(scout, Proposal);
        await scouted.MoveAsync(scout, JobStatus.AwaitingReview);
        scouted.Processes.Exits("npm", 0, TimeSpan.FromSeconds(20)).Exits("playwright", 1, TimeSpan.FromSeconds(5));

        await scouted.Desk.AcceptAsync(job, Cancellation);

        Assert.Equal(Found, scouted.Folders[Scouted.FakeJobs.Worktree(job), Scouted.ChecksPath]);
        Assert.Equal(DiscoveryState.Found, scouted.Book.Of(job).Match(found => found.State, () => DiscoveryState.Running));
        Assert.Equal([scout], scouted.Jobs.Discarded);
        Assert.Equal([job], scouted.Rechecked);
    }

    [Fact]
    public async Task SkippingTheProposedChecksRunsNothingAndLetsTheJobGoOnUnverifiedAsync()
    {
        await using var scouted = Scouted.Fresh();
        scouted.Policies.Autonomy = Autonomy.Supervised;
        var job = await scouted.RunningAsync();
        var scout = scouted.LastScout;
        await scouted.ProposesAsync(scout, Proposal);
        await scouted.MoveAsync(scout, JobStatus.AwaitingReview);

        await scouted.Desk.DeclineAsync(job, Cancellation);

        Assert.Empty(scouted.Processes.Requests);
        Assert.Null(scouted.Folders[Scouted.FakeJobs.Worktree(job), Scouted.ChecksPath]);
        Assert.Equal(DiscoveryState.NoneFound, scouted.Book.Of(job).Match(found => found.State, () => DiscoveryState.Running));
        Assert.Equal([scout], scouted.Jobs.Discarded);
        Assert.Equal([job], scouted.Rechecked);
    }

    [Fact]
    public async Task AProposalFromAnyoneButAScoutIsRefusedAndAMalformedOneIsAskedForAgainAsync()
    {
        await using var scouted = Scouted.Fresh();
        var job = await scouted.RunningAsync();

        var stranger = await scouted.ProposesAsync(job, Proposal);
        var malformed = await scouted.ProposesAsync(scouted.LastScout, """{ "checks": [{ "arguments": ["test"] }] }""");

        Assert.Equal((true, true), (stranger.IsError, malformed.IsError));
        Assert.Equal(string.Empty, scouted.Book.Of(job).Match(found => found.Proposal, () => "none"));
    }

    [Fact]
    public async Task WhatTheScoutAsksAPersonForIsDeniedAtOnceWhileTheJobsOwnRequestsWaitAsync()
    {
        await using var scouted = Scouted.Fresh();
        scouted.Policies.Autonomy = Autonomy.Supervised;
        var job = await scouted.RunningAsync();
        var scout = SessionId.New();

        await scouted.Proposals.HandleAsync(new Permissions.Contracts.PermissionDecided(Asked(scout, scouted.LastScout)), Cancellation);
        await scouted.Proposals.HandleAsync(new Permissions.Contracts.PermissionDecided(Asked(SessionId.New(), job)), Cancellation);

        var (session, reply) = Assert.Single(scouted.Answers.Given);
        Assert.Equal((scout, PermissionAnswer.Deny, Option<string>.Some(ProposalDesk.Refusal)), (session, reply.Answer, reply.Message));
    }

    [Fact]
    public async Task ADeclarationTheAgentAlreadyWroteInTheWorktreeIsNotOverwrittenAsync()
    {
        await using var scouted = Scouted.Fresh();
        var job = await scouted.RunningAsync();
        scouted.Folders.Draft(Scouted.FakeJobs.Worktree(job), Scouted.ChecksPath, """{ "checks": [] }""");
        await scouted.ProposesAsync(scouted.LastScout, Proposal);
        scouted.Processes.Exits("npm", 0, TimeSpan.FromSeconds(20));

        await scouted.MoveAsync(scouted.LastScout, JobStatus.AwaitingReview);

        Assert.Equal("""{ "checks": [] }""", scouted.Folders[Scouted.FakeJobs.Worktree(job), Scouted.ChecksPath]);
        Assert.Equal(DiscoveryState.Found, scouted.Book.Of(job).Match(found => found.State, () => DiscoveryState.Running));
    }

    [Theory]
    [InlineData("")]
    [InlineData("""{ "checks": [] }""")]
    [InlineData("""{ "checks": [{ "name": "e2e", "command": "playwright" }] }""")]
    public async Task AProposalWithNothingThatPassesFindsNoChecksAndShipsNothingAsync(string proposal)
    {
        await using var scouted = Scouted.Fresh();
        var job = await scouted.RunningAsync();

        if (proposal.Length > 0)
        {
            await scouted.ProposesAsync(scouted.LastScout, proposal);
        }

        scouted.Processes.Exits("playwright", 1, TimeSpan.FromSeconds(5));

        await scouted.MoveAsync(scouted.LastScout, JobStatus.AwaitingReview);

        Assert.Equal(DiscoveryState.NoneFound, scouted.Book.Of(job).Match(found => found.State, () => DiscoveryState.Running));
        Assert.Null(scouted.Folders[Scouted.FakeJobs.Worktree(job), Scouted.ChecksPath]);
        Assert.Equal([scouted.LastScout], scouted.Jobs.Discarded);
        Assert.Equal([job], scouted.Rechecked);
    }

    [Fact]
    public async Task AProposalIsCutToEightChecksOfThirtyMinutesAtMostAsync()
    {
        await using var scouted = Scouted.Fresh();
        var job = await scouted.RunningAsync();
        var checks = string.Join(", ", Enumerable.Range(1, 10).Select(number => $$"""{ "name": "step {{number}}", "command": "make", "timeoutSeconds": 86400 }"""));
        await scouted.ProposesAsync(scouted.LastScout, $$"""{ "checks": [{{checks}}] }""");
        scouted.Processes.Exits("make", 0, TimeSpan.FromSeconds(1));

        await scouted.MoveAsync(scouted.LastScout, JobStatus.AwaitingReview);

        var found = scouted.Book.Of(job).Match(discovery => discovery.Checks, () => []);
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
        var job = await scouted.RunningAsync();
        var scout = scouted.LastScout;

        await scouted.MoveAsync(scout, status);

        Assert.Equal(DiscoveryState.NoneFound, scouted.Book.Of(job).Match(found => found.State, () => DiscoveryState.Running));
        Assert.Equal(discarded ? [scout] : [], scouted.Jobs.Discarded);
        Assert.Equal([job], scouted.Rechecked);
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

        Assert.Equal(DiscoveryState.NoneFound, scouted.Book.Of(job).Match(found => found.State, () => DiscoveryState.Running));
        Assert.Equal([scouted.LastScout], scouted.Jobs.Discarded);
    }

    [Fact]
    public async Task ALaterJobInTheSameRepositoryShipsTheChecksAlreadyFoundWithoutAScoutAsync()
    {
        await using var scouted = Scouted.Fresh();
        _ = await scouted.RunningAsync();
        await scouted.ProposesAsync(scouted.LastScout, Proposal);
        scouted.Processes.Exits("npm", 0, TimeSpan.FromSeconds(20));
        await scouted.MoveAsync(scouted.LastScout, JobStatus.AwaitingReview);

        var later = await scouted.RunningAsync();
        var elsewhere = await scouted.RunningAsync(repository: "/repos/blog");

        Assert.Equal(2, scouted.Jobs.Submitted.Count);
        Assert.Equal(Option<JobId>.Some(elsewhere), scouted.Jobs.Submitted[^1].Request.Parent);
        Assert.Equal(DiscoveryState.Found, scouted.Book.Of(later).Match(discovery => discovery.State, () => DiscoveryState.Running));
        Assert.NotNull(scouted.Folders[Scouted.FakeJobs.Worktree(later), Scouted.ChecksPath]);
    }

    [Fact]
    public async Task AScoutThatFinishesBeforeItIsRecordedIsStillConcludedAsync()
    {
        await using var scouted = Scouted.Fresh();
        scouted.Jobs.SubmittedAs = JobStatus.AwaitingReview;
        var job = scouted.Job();

        await scouted.Desk.StartAsync(job, Cancellation);

        Assert.Equal(DiscoveryState.NoneFound, scouted.Book.Of(job).Match(found => found.State, () => DiscoveryState.Running));
        Assert.Equal([scouted.LastScout], scouted.Jobs.Discarded);
    }

    [Fact]
    public async Task ARefusedScoutFindsNoChecksAsync()
    {
        await using var scouted = Scouted.Fresh();
        scouted.Jobs.Rejection = JobRejection.ParentNotRunning;

        var job = await scouted.RunningAsync();

        Assert.Equal((DiscoveryState.NoneFound, Option<JobId>.None), scouted.Book.Of(job).Match(found => (found.State, found.Scout), () => default));
    }

    [Fact]
    public async Task AfterARestartAScoutThatFinishedWhileAvalaWasClosedIsConcludedAsync()
    {
        var scouted = Scouted.Fresh();
        var job = await scouted.RunningAsync();
        var scout = scouted.LastScout;
        await scouted.ProposesAsync(scout, Proposal);
        scouted.Jobs.Move(scout, JobStatus.AwaitingReview);
        scouted.Processes.Exits("npm", 0, TimeSpan.FromSeconds(20));

        await using var restarted = await scouted.RestartedAsync();

        Assert.Equal(DiscoveryState.Found, restarted.Book.Of(job).Match(discovery => discovery.State, () => DiscoveryState.Running));
        Assert.Equal([scout], restarted.Jobs.Discarded);
    }

    [Fact]
    public async Task AfterARestartAProposalStillWaitsForThePersonAsync()
    {
        var scouted = Scouted.Fresh();
        scouted.Policies.Autonomy = Autonomy.Supervised;
        var job = await scouted.RunningAsync();
        await scouted.ProposesAsync(scouted.LastScout, Proposal);
        await scouted.MoveAsync(scouted.LastScout, JobStatus.AwaitingReview);

        await using var restarted = await scouted.RestartedAsync();

        Assert.Equal(DiscoveryState.Proposed, restarted.Book.Of(job).Match(discovery => discovery.State, () => DiscoveryState.Running));
        Assert.Empty(restarted.Jobs.Discarded);
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
