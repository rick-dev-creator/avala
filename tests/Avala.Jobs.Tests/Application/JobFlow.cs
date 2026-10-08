using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Application;
using Avala.Jobs.Contracts;
using Avala.Jobs.Domain;
using Avala.Testing;
using JobAnnouncement = Avala.Jobs.Contracts.JobSubmitted;

namespace Avala.Jobs.Tests.Application;

internal sealed class JobFlow
{
    public JobFlow(FakeWorkspaces workspaces, FakeAgents agents, params ICompletionGate[] gates)
    {
        Workspaces = workspaces;
        Agents = agents;
        var ledger = new JobLedger(Store, Bus);
        var launcher = new JobLauncher(ledger, workspaces, agents);
        Submit = new SubmitJob(ledger, Bus);
        Jobs = new JobSubmissions(Submit);
        Prepare = new PrepareJob(ledger, launcher);
        Check = new CheckTurn(ledger, workspaces, new CompletionGates(gates), agents);
        Recovery = new JobRecovery(ledger, launcher);
    }

    public InMemoryJobStore Store { get; } = new();

    public RecordingBus Bus { get; } = new();

    public FakeWorkspaces Workspaces { get; }

    public FakeAgents Agents { get; }

    public SubmitJob Submit { get; }

    public IJobs Jobs { get; }

    public PrepareJob Prepare { get; }

    public CheckTurn Check { get; }

    public JobRecovery Recovery { get; }

    public static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public static JobFlow With(params ICompletionGate[] gates) => new(new FakeWorkspaces(), new FakeAgents(), gates);

    public async Task<Job> SubmittedAsync(int attemptsPerRound = 3)
    {
        var id = Outcomes.Succeeds(await Submit.ExecuteAsync("/repos/shop", "Add GitHub login", attemptsPerRound, Cancellation));

        return Store.Jobs.Single(job => job.Id == id);
    }

    public async Task<Job> RunningAsync(int attemptsPerRound = 3)
    {
        var job = await SubmittedAsync(attemptsPerRound);
        await Prepare.HandleAsync(new JobAnnouncement(job.Id), Cancellation);

        return job;
    }

    public ValueTask FinishTurnAsync(Job job, TurnOutcome outcome = TurnOutcome.Finished) =>
        job.Session.Match(
            session => Check.HandleAsync(new TurnFinished(session, TurnId.New(), outcome), Cancellation),
            () => ValueTask.CompletedTask);
}
