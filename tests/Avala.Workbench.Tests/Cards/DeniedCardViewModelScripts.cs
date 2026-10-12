using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workbench.Board;
using Avala.Workbench.Cards;

namespace Avala.Workbench.Tests.Cards;

public sealed class DeniedCardViewModelScripts
{
    private readonly Denying denying = new();
    private readonly FakeDeniedRequests denials = new();
    private readonly FakeJobs jobs = new() { Status = JobStatus.Running };

    [Fact]
    public void ADenialSaysWhatWasDeniedAndWhyAndOffersToAllowIt() =>
        ViewModelScript.Given(Card())
            .Then(card => Assert.Equal(
                ("Fetch https://example.com/style", "Nobody was watching, so the policy denied it.", true, false, true),
                (card.What, card.Why, card.CanAllow, card.IsAllowed, card.IsShown)));

    [Fact]
    public async Task AllowingKeepsTheRuleForTheJobAndTellsTheRunningAgentItMayRetry()
    {
        var card = Card();

        await card.AllowCommand.ExecuteAsync(null);

        Assert.Equal((denying.Session, new ItemId("fetch")), Assert.Single(denials.Allowed));
        Assert.Equal("steer Avala now allows web requests to example.com in this job. You may retry it.", Assert.Single(jobs.Calls));
        Assert.Equal((true, false, "Allowed for this job. Avala told the agent it may retry.", string.Empty), (card.IsAllowed, card.CanAllow, card.Notice, card.Error));
    }

    [Fact]
    public void ADenialAGuardKeepsForAPersonSaysSoAndOffersNoAllow() =>
        ViewModelScript.Given(new DeniedCardViewModel(denying.Entry(allowable: false), Replies()))
            .Then(card => Assert.Equal(
                ("A guard keeps this for a person, so it can't be allowed while the job runs on its own.", false),
                (card.Why, card.CanAllow)));

    [Fact]
    public async Task ARefusedAllowSaysWhyAndKeepsTheDenial()
    {
        denials.Refuse = true;
        var card = Card();

        await card.AllowCommand.ExecuteAsync(null);

        Assert.Equal((false, "The agent no longer waits for this answer.", string.Empty), (card.IsAllowed, card.Error, card.Notice));
        Assert.Empty(jobs.Calls);
    }

    private DeniedCardViewModel Card() => new(denying.Entry(), Replies());

    private Workbench.Replies.HumanReplies Replies()
    {
        var board = new JobBoard();
        board.Publish(Bench.Of(Bench.OnBoard(new JobSummary(denials.Job, "/repo", "Greet", DateTimeOffset.UnixEpoch, JobStatus.Running, Option<Agents.Contracts.Connections.ConnectionName>.None, Option<Autonomy>.None, Option<Workspaces.Contracts.WorkspaceId>.None))));

        return Replying.With(new FakePermissionAnswers(), new FakeAgents(), denials, jobs, board);
    }
}
