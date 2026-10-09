using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Supervision.Contracts;
using Avala.Supervision.Supervising;
using Avala.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Supervision.Tests.Supervising;

internal sealed class Supervised : IAsyncDisposable
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    private readonly SilenceAlarms alarms;
    private readonly Watchdog watchdog;
    private int dispatched;

    public Supervised(Option<JobRejection> rejection = default)
    {
        Jobs = new HoldingJobs(rejection);
        Book = new SupervisionBook(new FixedSettings());
        alarms = new SilenceAlarms(Clock, Bus);
        watchdog = new Watchdog(alarms, new FixedSettings(), new Intervener(Book, Jobs, Bus, Clock));
    }

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 9, 9, 0, 0, TimeSpan.Zero));

    public RecordingBus Bus { get; } = new();

    public HoldingJobs Jobs { get; }

    public SupervisionBook Book { get; }

    public JobId Job { get; } = JobId.New();

    public SessionId Session { get; } = SessionId.New();

    public TurnId Turn { get; } = TurnId.New();

    public IReadOnlyList<SupervisionIntervention> Interventions => Book.OfJob(Job);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async Task RunningAsync()
    {
        await ProgressAsync(JobStatus.Running);
        await JoinAsync(Session);
        await SeeAsync(new TurnStarted(Session, Turn));
    }

    public async Task ProgressAsync(JobStatus status) => await watchdog.HandleAsync(new JobProgressed(Job, status), Cancellation);

    public async Task JoinAsync(SessionId session) => await watchdog.HandleAsync(new JobSessionStarted(Job, session), Cancellation);

    public async Task SeeAsync(IAgentEvent agentEvent) => await watchdog.HandleAsync(new AgentActivity(agentEvent), Cancellation);

    public async Task AdvanceAsync(TimeSpan elapsed)
    {
        Clock.Advance(elapsed);

        foreach (var noticed in Bus.Published.OfType<SilenceNoticed>().Skip(dispatched).ToList())
        {
            dispatched++;
            await watchdog.HandleAsync(noticed, Cancellation);
        }
    }

    public ValueTask DisposeAsync() => alarms.DisposeAsync();

    internal sealed class HoldingJobs(Option<JobRejection> rejection) : IJobs
    {
        private readonly List<(JobId Job, HoldReason Reason)> holds = [];

        public IReadOnlyList<(JobId Job, HoldReason Reason)> Holds => holds;

        public ValueTask<Result<JobId, JobRejection>> SubmitAsync(JobRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<JobId, JobRejection>.Failure(JobRejection.InvalidRequest));

        public ValueTask<Result<JobHold, JobRejection>> HoldAsync(JobId job, HoldReason reason, CancellationToken cancellationToken)
        {
            holds.Add((job, reason));

            return ValueTask.FromResult(rejection.Match(
                Result<JobHold, JobRejection>.Failure,
                () => Result<JobHold, JobRejection>.Success(new JobHold(job, SessionId.New(), reason, SessionHalt.Interrupted))));
        }

        public ValueTask<Result<JobContinuation, JobRejection>> ContinueAsync(JobId job, string message, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<JobContinuation, JobRejection>.Failure(JobRejection.NotHeld));

        public ValueTask<Result<JobId, JobRejection>> DiscardAsync(JobId job, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<JobId, JobRejection>.Failure(JobRejection.NotDiscardable));
    }

    private sealed class FixedSettings : ISupervisionSettings
    {
        public ValueTask<SupervisionSettings> LoadAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(new SupervisionSettings(Window, SettingsFileStatus.Applied, Option<SupervisionError>.None));
    }
}
