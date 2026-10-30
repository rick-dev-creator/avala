using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Fleet.Connecting;
using Avala.Fleet.Facts;
using Avala.Fleet.Recording;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Verification.Contracts;
using Avala.Workspaces.Contracts;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Fleet.Tests.Recording;

public sealed class FleetRecorderTests : IAsyncDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private static readonly JobId Job = new(Guid.Parse("0199f6a0-0000-7000-8000-000000000100"));

    private static readonly SessionId Session = new(Guid.NewGuid());

    private readonly FakeFactStore store = new();

    private readonly FakeJobFacts jobs = new();

    private readonly FakeKeychain keychain = new();

    private readonly FakeTimeProvider clock = new(Noon);

    private readonly Outbox outbox;

    public FleetRecorderTests()
    {
        outbox = new Outbox(store, Microsoft.Extensions.Logging.Abstractions.NullLogger<Outbox>.Instance);
        jobs.Announcements[Job] = new JobAnnouncement("remote:github.com/rick/avala", Option<Guid>.None, "opus", "high", Noon.AddMinutes(-5));
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task NothingIsAppendedBeforeConnecting()
    {
        var recorder = await RecorderAsync(Option<FleetSettings>.None);

        await recorder.HandleAsync(new JobProgressed(Job, JobStatus.Running), Cancellation);

        Assert.Empty(store.Entries);
    }

    [Fact]
    public async Task NothingIsAppendedWhilePaused()
    {
        keychain.Keys[Fleets.Server] = Fleets.Key;
        var recorder = await RecorderAsync(Fleets.Settings(enabled: false));

        await recorder.HandleAsync(new JobProgressed(Job, JobStatus.Running), Cancellation);

        Assert.Empty(store.Entries);
    }

    [Fact]
    public async Task SessionsAreTrackedWhileDisconnected()
    {
        var file = new FakeFleetFile();
        var session = new FleetSession(file, keychain);
        await session.RunAsync(Cancellation);
        var recorder = new FleetRecorder(outbox, jobs, session, clock);
        await recorder.HandleAsync(new JobSessionStarted(Job, Session), Cancellation);
        await session.ConnectedAsync(Fleets.Settings(), Fleets.Key, Cancellation);

        await recorder.HandleAsync(Usage(100), Cancellation);

        Assert.Equal(Job.Value, Body<UsageReportedFact>(store.Entries[^1]).Job.Match(job => job, () => Guid.Empty));
    }

    [Fact]
    public async Task AJobIsAnnouncedBeforeItsFirstFact()
    {
        var recorder = await ConnectedRecorderAsync();

        await recorder.HandleAsync(new JobProgressed(Job, JobStatus.Running), Cancellation);
        await recorder.HandleAsync(new JobProgressed(Job, JobStatus.Checking), Cancellation);

        Assert.Equal([FactKinds.JobSubmitted, FactKinds.JobProgressed, FactKinds.JobProgressed], store.Entries.Select(entry => entry.Kind));
    }

    [Fact]
    public async Task AJobSubmittedBeforeConnectingIsAnnouncedWithItsSubmissionTime()
    {
        var recorder = await ConnectedRecorderAsync();

        await recorder.HandleAsync(new JobProgressed(Job, JobStatus.Running), Cancellation);

        var submitted = store.Entries[0];
        Assert.Equal(
            (Noon.AddMinutes(-5), "remote:github.com/rick/avala", "opus"),
            (submitted.Fact.OccurredAt, Body<JobSubmittedFact>(submitted).Repository, Body<JobSubmittedFact>(submitted).Model.Match(model => model, () => string.Empty)));
    }

    [Fact]
    public async Task AKeylessJobIsMarkedAndNotAnnounced()
    {
        jobs.Announcements[Job] = new JobAnnouncement(Option<string>.None, Option<Guid>.None, Option<string>.None, Option<string>.None, Noon);
        var recorder = await ConnectedRecorderAsync();

        await recorder.HandleAsync(new JobProgressed(Job, JobStatus.Running), Cancellation);

        Assert.Equal((FactKinds.JobProgressed, true), (string.Join(",", store.Entries.Select(entry => entry.Kind)), store.Announced.Contains((Fleets.Scope, Job.Value))));
    }

    [Fact]
    public async Task AnUnknownJobIsAnnouncedAtItsNextFact()
    {
        jobs.Announcements.Remove(Job);
        var recorder = await ConnectedRecorderAsync();
        await recorder.HandleAsync(new JobProgressed(Job, JobStatus.Running), Cancellation);
        jobs.Announcements[Job] = new JobAnnouncement("remote:github.com/rick/avala", Option<Guid>.None, Option<string>.None, Option<string>.None, Noon);

        await recorder.HandleAsync(new JobProgressed(Job, JobStatus.Checking), Cancellation);

        Assert.Equal([FactKinds.JobProgressed, FactKinds.JobSubmitted, FactKinds.JobProgressed], store.Entries.Select(entry => entry.Kind));
    }

    [Fact]
    public async Task AScratchJobsFactsAndUsageAreNeverAppended()
    {
        jobs.Scratch.Add(Job);
        var recorder = await ConnectedRecorderAsync();
        await recorder.HandleAsync(new JobSessionStarted(Job, Session), Cancellation);

        await recorder.HandleAsync(new JobProgressed(Job, JobStatus.Running), Cancellation);
        await recorder.HandleAsync(Usage(100), Cancellation);

        Assert.Empty(store.Entries);
    }

    [Fact]
    public async Task AJobTheCatalogDoesNotKnowYetIsNotTakenForAJobToShare()
    {
        jobs.Scratch.Add(Job);
        jobs.UnknownOnce.Add(Job);
        var recorder = await ConnectedRecorderAsync();

        await recorder.HandleAsync(new JobProgressed(Job, JobStatus.Running), Cancellation);
        await recorder.HandleAsync(new JobProgressed(Job, JobStatus.Checking), Cancellation);

        Assert.Empty(store.Entries);
    }

    [Fact]
    public async Task FactsOfAJobTheCatalogDoesNotKnowYetAreSentInOrderOnceItIsKnown()
    {
        jobs.UnknownOnce.Add(Job);
        var recorder = await ConnectedRecorderAsync();

        await recorder.HandleAsync(new JobProgressed(Job, JobStatus.Preparing), Cancellation);
        await recorder.HandleAsync(new JobProgressed(Job, JobStatus.Running), Cancellation);

        Assert.Equal(
            ["jobSubmitted", "preparing", "running"],
            store.Entries.Select(entry => entry.Kind == FactKinds.JobSubmitted ? "jobSubmitted" : Body<JobProgressedFact>(entry).Status));
    }

    [Fact]
    public async Task FactsHeldForOneServerAreNotSentToTheNextOne()
    {
        jobs.UnknownOnce.Add(Job);
        var session = await Fleets.ConnectedAsync(keychain);
        var recorder = new FleetRecorder(outbox, jobs, session, clock);
        await recorder.HandleAsync(new JobProgressed(Job, JobStatus.Preparing), Cancellation);
        var next = Fleets.Settings() with { Instance = Guid.NewGuid() };
        await session.ConnectedAsync(next, Fleets.Key, Cancellation);

        await recorder.HandleAsync(new JobProgressed(Job, JobStatus.Running), Cancellation);

        Assert.Equal(
            ["jobSubmitted", "running"],
            store.Entries.Select(entry => entry.Kind == FactKinds.JobSubmitted ? "jobSubmitted" : Body<JobProgressedFact>(entry).Status));
    }

    [Fact]
    public async Task AnEndedSessionIsForgotten()
    {
        var recorder = await ConnectedRecorderAsync();
        await recorder.HandleAsync(new JobSessionStarted(Job, Session), Cancellation);
        await recorder.HandleAsync(new SessionEnded(Session, SessionEnding.Closed), Cancellation);

        await recorder.HandleAsync(Usage(100), Cancellation);

        Assert.True(Body<UsageReportedFact>(store.Entries[^1]).Job.IsNone);
    }

    [Fact]
    public async Task UsageCarriesTheSessionsLatestModelAndRoundedCost()
    {
        var recorder = await ConnectedRecorderAsync();
        await recorder.HandleAsync(new JobSessionStarted(Job, Session), Cancellation);
        await recorder.HandleAsync(new AgentActivity(new ModelReported(Session, new TurnId(Guid.NewGuid()), "claude-opus-5-5")), Cancellation);

        await recorder.HandleAsync(Usage(100, 0.0022790500000000003m), Cancellation);

        var usage = Body<UsageReportedFact>(store.Entries[^1]);
        Assert.Equal(
            ("claude-opus-5-5", 0.002279m, 100L),
            (usage.Model.Match(model => model, () => string.Empty), usage.Cost.Match(cost => cost.Amount, () => -1m), usage.Tokens.Input));
    }

    [Fact]
    public async Task UsageOfASessionWithoutAJobHasNoJob()
    {
        var recorder = await ConnectedRecorderAsync();

        await recorder.HandleAsync(Usage(7), Cancellation);

        Assert.True(Body<UsageReportedFact>(Assert.Single(store.Entries)).Job.IsNone);
    }

    [Fact]
    public async Task EachKindIsMappedFromItsEvent()
    {
        var recorder = await ConnectedRecorderAsync();

        await recorder.HandleAsync(new JobHeld(new JobHold(Job, Option<SessionId>.None, HoldReason.BudgetExceeded, SessionHalt.Interrupted)), Cancellation);
        await recorder.HandleAsync(new JobModelChosen(Job, new ModelChoice("sonnet", "low")), Cancellation);
        await recorder.HandleAsync(new AttemptVerified(new VerificationReport(Job, 2, VerificationOutcome.NoChecksDeclared, Option<FileOrigin>.None, [], GateVerdict.Pass, Noon.AddMinutes(1))), Cancellation);

        Assert.Equal(
            ["budgetExceeded", "sonnet low", "2 noChecksDeclared"],
            [
                Body<JobHeldFact>(store.Entries[1]).Reason,
                $"{Body<JobModelChosenFact>(store.Entries[2]).Model.Match(model => model, () => string.Empty)} {Body<JobModelChosenFact>(store.Entries[2]).Effort.Match(effort => effort, () => string.Empty)}",
                $"{Body<AttemptVerifiedFact>(store.Entries[3]).Attempt} {Body<AttemptVerifiedFact>(store.Entries[3]).Outcome}",
            ]);
    }

    [Fact]
    public async Task AStampNeverGoesBeforeTheLatestStoredOne()
    {
        store.Announced.Add((Fleets.Scope, Job.Value));
        store.Entries.Add(new StoredEntry(Fleets.Scope, new Fact(Guid.NewGuid(), Noon.AddMinutes(10), new JobProgressedFact(Job.Value, "running"))));
        var recorder = await ConnectedRecorderAsync();

        await recorder.HandleAsync(new JobProgressed(Job, JobStatus.Checking), Cancellation);

        Assert.Equal(Noon.AddMinutes(10), store.Entries[^1].Fact.OccurredAt);
    }

    [Fact]
    public async Task ANewScopeAnnouncesItsJobsAgain()
    {
        var recorder = await ConnectedRecorderAsync();
        await recorder.HandleAsync(new JobProgressed(Job, JobStatus.Running), Cancellation);
        store.Announced.Clear();

        await recorder.HandleAsync(new JobProgressed(Job, JobStatus.Checking), Cancellation);

        Assert.Equal(2, store.Entries.Count(entry => entry.Kind == FactKinds.JobSubmitted));
    }

    public ValueTask DisposeAsync() => outbox.DisposeAsync();

    private static T Body<T>(StoredEntry entry)
        where T : IFactBody => (T)entry.Fact.Body;

    private static AgentActivity Usage(long input, decimal? cost = null) =>
        new(new UsageReported(
            Session,
            new TurnId(Guid.NewGuid()),
            new TokenUsage(input, 1, 0, 0, 0),
            cost is { } amount ? new Cost(amount, "USD") { Basis = CostBasis.ApiEquivalent } : Option<Cost>.None));

    private async Task<FleetRecorder> RecorderAsync(Option<FleetSettings> settings) =>
        new(outbox, jobs, await Fleets.SessionAsync(keychain, settings), clock);

    private async Task<FleetRecorder> ConnectedRecorderAsync() =>
        new(outbox, jobs, await Fleets.ConnectedAsync(keychain), clock);
}
