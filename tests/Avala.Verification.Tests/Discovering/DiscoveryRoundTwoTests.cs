using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Verification.Contracts;
using Avala.Verification.Discovering;

namespace Avala.Verification.Tests.Discovering;

public sealed class DiscoveryRoundTwoTests
{
    private const string Proposal = """{ "checks": [{ "name": "tests", "command": "npm", "arguments": ["test"] }] }""";

    private static CancellationToken Cancellation => Scouted.Cancellation;

    [Fact]
    public async Task AGrandchildDeferredWhileTheChecksAreFoundIsRecheckedOnceTheyAreFoundAsync()
    {
        await using var scouted = Scouted.Fresh();
        var root = await scouted.RunningAsync();
        var child = scouted.Job(parent: root);
        var grandchild = scouted.Job(parent: child);

        var deferred = await scouted.EvaluateAsync(grandchild);
        await FoundAsync(scouted);

        Assert.Equal(GateVerdict.Deferred, deferred);
        Assert.Contains(grandchild, scouted.Rechecked);
    }

    [Fact]
    public async Task AChildIsNotPassedUnverifiedBeforeItsParentsDiscoveryIsRecordedAsync()
    {
        await using var scouted = Scouted.Fresh();
        var parent = scouted.Job();
        var child = scouted.Job(parent: parent);

        var verdict = await scouted.EvaluateAsync(child);

        Assert.Equal(GateVerdict.Deferred, verdict);
        Assert.Equal(Option<JobId>.Some(parent), Assert.Single(scouted.Jobs.Submitted).Request.Parent);
    }

    [Fact]
    public async Task AfterThePersonSkipsTheProposedChecksALaterJobIsNotHeldForAnotherChoiceAsync()
    {
        await using var scouted = Scouted.Fresh();
        scouted.Policies.Autonomy = Autonomy.Supervised;
        var first = await scouted.RunningAsync();
        await scouted.ProposesAsync(scouted.LastScout, Proposal);
        await scouted.MoveAsync(scouted.LastScout, JobStatus.AwaitingReview);
        await scouted.Desk.DeclineAsync(first, Cancellation);
        scouted.Clock.Advance(TimeSpan.FromMinutes(1));

        var later = await scouted.RunningAsync();

        Assert.Equal((1, GateDecision.Pass), (scouted.Jobs.Submitted.Count, (await scouted.EvaluateAsync(later)).Decision));
    }

    [Fact]
    public async Task AScoutThatEndedEarlyLeavesALaterJobToScoutAgainAsync()
    {
        await using var scouted = Scouted.Fresh();
        var first = await scouted.RunningAsync();
        await scouted.MoveAsync(first, JobStatus.Discarded);
        scouted.Clock.Advance(TimeSpan.FromMinutes(1));

        _ = await scouted.RunningAsync();

        Assert.Equal(2, scouted.Jobs.Submitted.Count);
    }

    [Fact]
    public async Task AJobThatFollowsAProposalShowsItAndCanUseItAsync()
    {
        await using var scouted = Scouted.Fresh();
        scouted.Policies.Autonomy = Autonomy.Supervised;
        _ = await scouted.RunningAsync();
        scouted.Clock.Advance(TimeSpan.FromSeconds(1));
        var follower = await scouted.RunningAsync();
        await scouted.ProposesAsync(scouted.LastScout, Proposal);
        await scouted.MoveAsync(scouted.LastScout, JobStatus.AwaitingReview);
        var shown = scouted.Book.OfJob(follower).Match(found => found.State, () => DiscoveryState.Running);
        scouted.Processes.Exits("npm", 0, TimeSpan.FromSeconds(8));

        await scouted.Desk.AcceptAsync(follower, Cancellation);

        Assert.Equal(DiscoveryState.Proposed, shown);
        Assert.Equal(DiscoveryState.Found, scouted.Book.OfJob(follower).Match(found => found.State, () => DiscoveryState.Running));
        Assert.Contains(scouted.Bus.Published.OfType<ChecksDiscoveryChanged>(), changed => changed.Discovery.Job == follower && changed.Discovery.State == DiscoveryState.Proposed);
    }

    [Fact]
    public async Task WhenTheJobAProposalWaitsOnEndsTheJobThatFollowedItInheritsTheProposalAsync()
    {
        await using var scouted = Scouted.Fresh();
        scouted.Policies.Autonomy = Autonomy.Supervised;
        var first = await scouted.RunningAsync();
        scouted.Clock.Advance(TimeSpan.FromSeconds(1));
        var follower = await scouted.RunningAsync();
        await scouted.ProposesAsync(scouted.LastScout, Proposal);
        await scouted.MoveAsync(scouted.LastScout, JobStatus.AwaitingReview);

        await scouted.MoveAsync(first, JobStatus.Discarded);
        scouted.Processes.Exits("npm", 0, TimeSpan.FromSeconds(8));
        await scouted.Desk.AcceptAsync(follower, Cancellation);

        Assert.Single(scouted.Jobs.Submitted);
        Assert.Equal(DiscoveryState.Found, scouted.Book.OfJob(follower).Match(found => found.State, () => DiscoveryState.Running));
        Assert.Equal(Option<string>.Some(Scouted.FakeJobs.Worktree(follower)), scouted.Processes.Requests[^1].WorkingDirectory);
    }

    [Fact]
    public async Task OnlyCommandsThePolicyLetTheScoutRunAreRunAgainAsTheRepositorysChecksAsync()
    {
        await using var scouted = Scouted.Fresh();
        var job = await scouted.RunningAsync();
        await scouted.RanAsync(scouted.LastScout, "npm   test");
        await scouted.ProposesAsync(scouted.LastScout, """{ "checks": [{ "name": "tests", "command": "npm", "arguments": ["test"] }, { "name": "wipe", "command": "rm", "arguments": ["-rf", "/"] }] }""", ran: false);
        scouted.Processes.Exits("npm", 0, TimeSpan.FromSeconds(8)).Exits("rm", 0, TimeSpan.FromSeconds(1));

        await scouted.MoveAsync(scouted.LastScout, JobStatus.AwaitingReview);

        Assert.Equal(["npm"], scouted.Processes.Requests.Select(request => request.FileName));
        Assert.Equal(["tests"], scouted.Book.Of(job).Match(found => found.Checks.Select(check => check.Name).ToArray(), () => []));
    }

    [Fact]
    public async Task AQuestionTheScoutAsksAPersonIsDeclinedAtOnceAsync()
    {
        await using var scouted = Scouted.Fresh();
        scouted.Policies.Autonomy = Autonomy.Supervised;
        var job = await scouted.RunningAsync();
        var scout = SessionId.New();
        var form = new AgentForm(FormPurpose.Question, "Which tests?", "Choose", []);

        await scouted.Proposals.HandleAsync(new Permissions.Contracts.FormDecided(Asked(scout, scouted.LastScout, form)), Cancellation);
        await scouted.Proposals.HandleAsync(new Permissions.Contracts.FormDecided(Asked(SessionId.New(), job, form)), Cancellation);

        var (session, answer) = Assert.Single(scouted.Agents.Forms);
        Assert.Equal((scout, true, Option<string>.Some(ProposalDesk.Unasked), MessageAuthor.Avala), (session, answer.Declined, answer.Message, answer.AnsweredBy));
    }

    private static Permissions.Contracts.FormDecision Asked(SessionId session, JobId job, AgentForm form) =>
        new(session, TurnId.New(), new ItemId("question"), job, form, Autonomy.Supervised, Option<FormAnswer>.None, [], Permissions.Contracts.DecisionDelivery.LeftToHuman, DateTimeOffset.UnixEpoch);

    private static async Task FoundAsync(Scouted scouted)
    {
        await scouted.ProposesAsync(scouted.LastScout, Proposal);
        scouted.Processes.Exits("npm", 0, TimeSpan.FromSeconds(8));
        await scouted.MoveAsync(scouted.LastScout, JobStatus.AwaitingReview);
    }
}
