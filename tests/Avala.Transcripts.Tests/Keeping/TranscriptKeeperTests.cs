using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Canvas.Contracts;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Transcripts.Contracts;
using Avala.Transcripts.Keeping;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Transcripts.Tests.Keeping;

public sealed class TranscriptKeeperTests
{
    private static readonly SessionId Session = SessionId.New();
    private static readonly TurnId Turn = TurnId.New();
    private static readonly ItemId Item = new("item");

    private readonly JobId job = JobId.New();
    private readonly FakeLog log = new();
    private readonly FakeCatalog catalog = new();
    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero));
    private readonly TranscriptKeeper keeper;

    public TranscriptKeeperTests() => keeper = new TranscriptKeeper(catalog, log, clock);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WhatAJobsSessionsShowIsKeptInOrderWithTheTimeItArrivedAsync()
    {
        await keeper.HandleAsync(new JobSessionStarted(job, Session), Cancellation);
        var started = new ItemStarted(Session, Turn, Item, ItemKind.Command, "dotnet test") { Input = "dotnet test" };
        var snapshot = new CanvasSnapshot(new CanvasId(Turn, new ItemId("canvas")), Session, "Flow", "image/svg+xml", "<svg/>", CanvasStatus.Completed, true);
        var decision = new PolicyDecision(Session, Turn, Item, job, ItemKind.Command, "dotnet test", PolicyAnswer.Allow, Option<PolicyRule>.None, DecisionDelivery.Answered, DateTimeOffset.UnixEpoch);
        var form = new FormDecision(Session, Turn, Item, job, new AgentForm(FormPurpose.Question, "Which database?", string.Empty, []), Autonomy.Supervised, Option<FormAnswer>.None, [], DecisionDelivery.LeftToHuman, DateTimeOffset.UnixEpoch);

        await keeper.HandleAsync(new AgentActivity(started), Cancellation);
        clock.Advance(TimeSpan.FromSeconds(2));
        await keeper.HandleAsync(new CanvasUpdated(snapshot), Cancellation);
        await keeper.HandleAsync(new PermissionDecided(decision), Cancellation);
        await keeper.HandleAsync(new FormDecided(form), Cancellation);

        Assert.Equal(
            [
                (job, clock.Start, (ITranscriptFact)new AgentActed(started)),
                (job, clock.Start.AddSeconds(2), new CanvasDrawn(snapshot)),
                (job, clock.Start.AddSeconds(2), new PermissionRuled(decision)),
                (job, clock.Start.AddSeconds(2), new FormRuled(form)),
            ],
            log.Kept);
    }

    [Fact]
    public async Task TheEventsOfASessionNoJobStartedAreNotKeptAsync()
    {
        await keeper.HandleAsync(new AgentActivity(new TurnStarted(Session, Turn)), Cancellation);

        Assert.Empty(log.Kept);
    }

    [Fact]
    public async Task LimitsAndResumeTokensAreNotKeptSinceTheConversationDoesNotShowThemAsync()
    {
        await keeper.HandleAsync(new JobSessionStarted(job, Session), Cancellation);

        await keeper.HandleAsync(new AgentActivity(new LimitReported(Session, Turn, new UsageLimit("5h", 0.5, Option<DateTimeOffset>.None))), Cancellation);
        await keeper.HandleAsync(new AgentActivity(new ResumeTokenIssued(Session, Turn, new ResumeToken("conversation"))), Cancellation);

        Assert.Empty(log.Kept);
    }

    [Fact]
    public async Task EachAttemptIsMarkedOnceWhenTheJobFirstProgressesWithItAsync()
    {
        catalog.Attempts[job] = 1;
        await keeper.HandleAsync(new JobProgressed(job, JobStatus.Running), Cancellation);
        await keeper.HandleAsync(new JobProgressed(job, JobStatus.Checking), Cancellation);
        catalog.Attempts[job] = 2;

        await keeper.HandleAsync(new JobProgressed(job, JobStatus.Running), Cancellation);

        Assert.Equal([new AttemptBegan(1), new AttemptBegan(2)], log.Facts);
    }

    [Fact]
    public async Task AJobOfAnEarlierRunMarksOnlyTheAttemptsItsTranscriptLacksAsync()
    {
        log.Earlier[job] = 2;
        catalog.Attempts[job] = 3;

        await keeper.HandleAsync(new JobProgressed(job, JobStatus.Running), Cancellation);

        Assert.Equal([new AttemptBegan(3)], log.Facts);
    }

    [Fact]
    public async Task AnItemsStreamedTextIsKeptUpToTheCapThenSaysTheRestWasNotKeptAsync()
    {
        await keeper.HandleAsync(new JobSessionStarted(job, Session), Cancellation);
        await keeper.HandleAsync(new AgentActivity(new ItemStarted(Session, Turn, Item, ItemKind.Command, "dotnet test")), Cancellation);

        foreach (var text in new[] { new string('a', 20_000), new string('b', 20_000), "c" })
        {
            await keeper.HandleAsync(new AgentActivity(new ItemProgressed(Session, Turn, Item, text)), Cancellation);
        }

        var kept = string.Concat(log.Facts.OfType<AgentActed>().Select(acted => acted.Event).OfType<ItemProgressed>().Select(progressed => progressed.Text));
        Assert.Equal(new string('a', 20_000) + new string('b', TranscriptBounds.ItemText - 20_000) + TranscriptBounds.Cut, kept);
    }

    [Fact]
    public async Task ToolInputsAndResultsAreCutAtTheCapAsync()
    {
        var large = new string('x', TranscriptBounds.ItemText + 1);
        var cut = new string('x', TranscriptBounds.ItemText) + TranscriptBounds.Cut;
        await keeper.HandleAsync(new JobSessionStarted(job, Session), Cancellation);

        await keeper.HandleAsync(new AgentActivity(new ItemStarted(Session, Turn, Item, ItemKind.Command, "cat") { Input = large }), Cancellation);
        await keeper.HandleAsync(new AgentActivity(new ToolCalled(Session, Turn, new ItemId("tool"), "delegate", large)), Cancellation);
        await keeper.HandleAsync(new AgentActivity(new ToolReturned(Session, Turn, new ItemId("tool"), new ToolResult(new ItemId("tool"), large))), Cancellation);

        var events = log.Facts.OfType<AgentActed>().Select(acted => acted.Event).ToList();
        Assert.Equal(
            (Option<string>.Some(cut), cut, cut),
            (((ItemStarted)events[0]).Input, ((ToolCalled)events[1]).Input, ((ToolReturned)events[2]).Result.Content));
    }
}
