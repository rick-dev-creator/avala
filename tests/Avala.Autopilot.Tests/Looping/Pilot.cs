using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Autopilot.Approving;
using Avala.Autopilot.Contracts;
using Avala.Autopilot.Evidence;
using Avala.Autopilot.Looping;
using Avala.Autopilot.Sourcing;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Verification.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Autopilot.Tests.Looping;

internal sealed class Pilot : IAsyncDisposable
{
    public static readonly string Repository = Path.GetFullPath("/repositories/shop");

    public static AutopilotRules Clean { get; } = new(ApprovalRule.CleanEvidence, FollowUpRule.Refuse);

    public Pilot(params IJobSource[] others)
    {
        var gatherer = new EvidenceGatherer(Evidence, Evidence, Jobs, new JobWork(Work, Work, Rules));
        var steps = new LoopSteps(Jobs, new TaskSources([Backlog, .. others]), new AutoApprover(gatherer, Jobs, Bus, Clock), new LoopGauges(Usage, Usage));
        Registry = new LoopRegistry(steps, new LoopJournal(Bus, new LoopBook(), NullLogger<LoopJournal>.Instance), Clock);
        Feed = new LoopFeed(Registry);
    }

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 9, 22, 0, 0, TimeSpan.Zero));

    public RecordingBus Bus { get; } = new();

    public FakeJobs Jobs { get; } = new();

    public FakeEvidence Evidence { get; } = new();

    public FakeWork Work { get; } = new();

    public FakeUsage Usage { get; } = new();

    public FixedRules Rules { get; } = new(Clean);

    public MemorySource Backlog { get; } = new(BacklogSource.Source);

    public LoopRegistry Registry { get; }

    public LoopFeed Feed { get; }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async Task<LoopId> StartAsync(LoopLimits? limits = null) =>
        Outcomes.Succeeds(await Registry.StartAsync(new LoopRequest(Repository) { Limits = limits ?? new LoopLimits() }, Cancellation));

    public async Task<JobId> TakenAsync(int iteration) =>
        (await Bus.WaitForAsync<LoopTaskTaken>(taken => taken.Iteration == iteration, Cancellation)).Job;

    public async Task<IterationRecord> IteratedAsync(int iteration) =>
        (await Bus.WaitForAsync<LoopIterated>(iterated => iterated.Iteration.Number == iteration, Cancellation)).Iteration;

    public async Task<LoopState> EndedAsync() => (await Bus.WaitForAsync<LoopEnded>(_ => true, Cancellation)).State;

    public async Task<IterationRecord> CleanAsync(int iteration, params string[] files)
    {
        var job = await TakenAsync(iteration);
        Passed(job);
        Work.Changed(job, files.Length == 0 ? ["GREETING.md"] : files);
        await ProgressAsync(job, JobStatus.AwaitingReview);

        return await IteratedAsync(iteration);
    }

    public async Task<IterationRecord> FailingAsync(int iteration, string check, int exitCode)
    {
        var job = await TakenAsync(iteration);
        Evidence.Verified(job, Report(job, VerificationOutcome.Failed, new CheckEvidence(check, check, CheckStatus.Failed, exitCode, TimeSpan.FromSeconds(1), string.Empty, string.Empty)));
        Jobs.Attempts(job, Attempt(1, AttemptOrigin.Initial, AttemptOutcome.Rejected));
        Work.Changed(job, "calculator.txt");
        await ProgressAsync(job, JobStatus.NeedsHelp);

        return await IteratedAsync(iteration);
    }

    public void Passed(JobId job)
    {
        Evidence.Verified(job, Report(job, VerificationOutcome.Passed, new CheckEvidence("tests", "dotnet test", CheckStatus.Passed, 0, TimeSpan.FromSeconds(1), string.Empty, string.Empty)));
        Jobs.Attempts(job, Attempt(1, AttemptOrigin.Initial, AttemptOutcome.Passed));
    }

    public ValueTask ProgressAsync(JobId job, JobStatus status) => Feed.HandleAsync(new JobProgressed(job, status), Cancellation);

    public static AttemptRecord Attempt(int number, AttemptOrigin origin, AttemptOutcome outcome) =>
        new(number, origin, outcome, Option<string>.None, Option<SessionId>.None);

    public static VerificationReport Report(JobId job, VerificationOutcome outcome, params CheckEvidence[] checks) =>
        new(job, 1, outcome, Option<Workspaces.Contracts.FileOrigin>.None, checks, GateVerdict.Pass, DateTimeOffset.UnixEpoch);

    public static Cost Usd(decimal amount) => new(amount, "USD");

    public ValueTask DisposeAsync() => Registry.DisposeAsync();
}
