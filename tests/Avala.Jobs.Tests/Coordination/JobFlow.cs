using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Catalog;
using Avala.Jobs.Contracts;
using Avala.Jobs.Holding;
using Avala.Jobs.Jobs;
using Avala.Jobs.Launching;
using Avala.Jobs.Ledger;
using Avala.Jobs.Recovery;
using Avala.Jobs.Submission;
using Avala.Jobs.TurnChecks;
using Avala.Sdk;
using Avala.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using JobAnnouncement = Avala.Jobs.Contracts.JobSubmitted;

namespace Avala.Jobs.Tests.Coordination;

internal sealed class JobFlow
{
    public JobFlow(FakeWorkspaces workspaces, FakeAgents agents, params ICompletionGate[] gates)
    {
        Workspaces = workspaces;
        Agents = agents;
        var ledger = new JobLedger(Store, Bus, agents);
        var launcher = new JobLauncher(ledger, workspaces, agents, Defaults);
        Queues = new JobQueues(ledger, NullLogger<JobQueues>.Instance);
        Submit = new SubmitJob(ledger, Bus, Connections, Clock);
        Hold = new HoldJob(ledger, agents, Bus);
        Jobs = new JobsEntry(Submit, Hold, launcher, Queues);
        Prepare = new PrepareJob(Queues, launcher, Admissions);
        Check = new CheckTurn(ledger, Queues, new EvaluateTurn(ledger, workspaces, new CompletionGates(gates), agents), Hold);
        Recovery = new JobRecovery(ledger, Queues, launcher);
        Catalog = new JobCatalog(Store);
    }

    public IJobCatalog Catalog { get; }

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 9, 8, 0, 0, TimeSpan.Zero)) { AutoAdvanceAmount = TimeSpan.FromSeconds(1) };

    public List<IJobAdmission> Admissions { get; } = [];

    public InMemoryJobStore Store { get; } = new();

    public FakeConnections Connections { get; } = new();

    public FakeRepositoryDefaults Defaults { get; } = new();

    public RecordingBus Bus { get; } = new();

    public FakeWorkspaces Workspaces { get; }

    public FakeAgents Agents { get; }

    public JobQueues Queues { get; }

    public SubmitJob Submit { get; }

    public HoldJob Hold { get; }

    public IJobs Jobs { get; }

    public PrepareJob Prepare { get; }

    public CheckTurn Check { get; }

    public JobRecovery Recovery { get; }

    public static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public static JobFlow With(params ICompletionGate[] gates) => new(new FakeWorkspaces(), new FakeAgents(), gates);

    public static JobRequest Request(int attemptsPerRound = 3) => new("/repos/shop", "Add GitHub login", attemptsPerRound);

    public async Task<Job> SubmittedAsync(int attemptsPerRound = 3, Option<Autonomy> autonomy = default) =>
        await SubmittedAsync(Request(attemptsPerRound) with { Autonomy = autonomy });

    public async Task<Job> SubmittedAsync(JobRequest request)
    {
        var id = Outcomes.Succeeds(await Submit.ExecuteAsync(request, Cancellation));

        return Store.Jobs.Single(job => job.Id == id);
    }

    public async Task<Job> RunningAsync(int attemptsPerRound = 3, Option<Autonomy> autonomy = default) =>
        await RunningAsync(Request(attemptsPerRound) with { Autonomy = autonomy });

    public async Task<Job> RunningAsync(JobRequest request)
    {
        var job = await SubmittedAsync(request);
        await Prepare.HandleAsync(new JobAnnouncement(job.Id), Cancellation);
        await SettledAsync(job);

        return job;
    }

    public async Task FinishTurnAsync(Job job, TurnOutcome outcome = TurnOutcome.Finished)
    {
        await HandTurnAsync(job, outcome);
        await SettledAsync(job);
    }

    public async Task HandTurnAsync(Job job, TurnOutcome outcome = TurnOutcome.Finished) =>
        await Check.HandleAsync(new TurnFinished(Session(job), TurnId.New(), outcome), Cancellation);

    public async Task EndSessionAsync(Job job, SessionId session)
    {
        await Check.HandleAsync(new SessionEnded(session, SessionEnding.Crashed), Cancellation);
        await SettledAsync(job);
    }

    public async Task OfferResumeAsync(Job job, SessionId session, ResumeToken token)
    {
        await Check.HandleAsync(new SessionResumable(session, token), Cancellation);
        await SettledAsync(job);
    }

    public async Task<Job> HeldAsync(HoldReason reason, Option<ResumeToken> resumable = default)
    {
        var job = await RunningAsync();

        await resumable.Match(token => OfferResumeAsync(job, Session(job), token), () => Task.CompletedTask);
        Outcomes.Succeeds(await Jobs.HoldAsync(job.Id, reason, Cancellation));

        return job;
    }

    public async Task SettledAsync(Job job) =>
        _ = await Queues.RunAsync(job.Id, (_, _) => Task.FromResult(true), Cancellation);

    private static SessionId Session(Job job) => Outcomes.Present(job.Session);
}
