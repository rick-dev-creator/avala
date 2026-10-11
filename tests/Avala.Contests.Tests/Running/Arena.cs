using Avala.Contests.Contracts;
using Avala.Contests.Running;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Contests.Tests.Running;

internal sealed class Arena : IAsyncDisposable
{
    public const string Repository = "/code/shop-api";

    private readonly ContestBook book;

    public Arena()
    {
        book = new ContestBook(Store, Bus);
        var judge = new ContestJudge(new FactGatherer(Runs, Jobs, Runs, Runs), book, Clock);
        Desk = new ContestDesk(book, Jobs, judge, Jobs);
        Contests = new ContestsEntry(book, new ContestStarter(book, Jobs, new CandidateConnections(Connections, Connections), Clock), Store);
    }

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 11, 9, 0, 0, TimeSpan.Zero));

    public RecordingBus Bus { get; } = new();

    public MemoryContests Store { get; } = new();

    public FakeJobs Jobs { get; } = new();

    public FakeRuns Runs { get; } = new();

    public FakeConnections Connections { get; } = new();

    public ContestDesk Desk { get; }

    public ContestsEntry Contests { get; }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async Task<ContestRecord> StartAsync(int candidates) =>
        Outcomes.Succeeds(await Contests.StartAsync(new ContestRequest(new JobRequest(Repository, "Fix the calculator"), candidates), Cancellation));

    public async Task<ContestRecord> MovedAsync(JobId job, JobStatus status, Option<HoldReason> hold = default)
    {
        Jobs.Moved(job, status, hold);
        await Desk.HandleAsync(new JobProgressed(job, status), Cancellation);

        return Outcomes.Present(Contests.OfJob(job));
    }

    public async Task<ContestRecord> AwaitingReviewAsync(JobId job, RunFacts facts)
    {
        Runs.Ran(job, facts);
        Clock.Advance(TimeSpan.FromMinutes(1));

        return await MovedAsync(job, JobStatus.AwaitingReview);
    }

    public (ContestId Contest, ContestStatus Status) Announced<TEvent>(Func<TEvent, ContestRecord> contest)
        where TEvent : IIntegrationEvent
    {
        var announced = contest(Assert.Single(Bus.Published.OfType<TEvent>()));

        return (announced.Contest, announced.Status);
    }

    public ValueTask DisposeAsync() => book.DisposeAsync();
}
