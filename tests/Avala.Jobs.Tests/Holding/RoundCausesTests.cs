using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Jobs.Holding;
using Avala.Jobs.Jobs;
using Avala.Jobs.Ledger;
using Avala.Jobs.Tests.Coordination;
using Avala.Testing;

namespace Avala.Jobs.Tests.Holding;

public sealed class RoundCausesTests
{
    private static CancellationToken Cancellation => JobFlow.Cancellation;

    [Fact]
    public async Task APersonJoiningAChildsConversationJoinsTheRoundsOfTheChildAndItsParentsAsync()
    {
        var flow = JobFlow.With();
        var parent = await WokenAsync(flow, JobFlow.Request());
        var child = await WokenAsync(flow, JobFlow.Request() with { Parent = parent.Id });
        var rounds = new RoundCauses(new JobLedger(flow.Store, flow.Bus, flow.Agents, flow.Clock), flow.Queues, flow.Hold);

        await rounds.PersonJoinedAsync(Outcomes.Present(child.Session), Cancellation);

        Assert.Equal((MessageAuthor.Person, MessageAuthor.Person), (child.Attempts[^1].Cause, parent.Attempts[^1].Cause));
    }

    [Fact]
    public async Task APersonJoiningASessionOfNoJobChangesNothingAsync()
    {
        var flow = JobFlow.With();
        var job = await WokenAsync(flow, JobFlow.Request());
        var rounds = new RoundCauses(new JobLedger(flow.Store, flow.Bus, flow.Agents, flow.Clock), flow.Queues, flow.Hold);

        await rounds.PersonJoinedAsync(SessionId.New(), Cancellation);

        Assert.Equal(MessageAuthor.Avala, job.Attempts[^1].Cause);
    }

    private static async Task<Job> WokenAsync(JobFlow flow, JobRequest request)
    {
        var job = await flow.RunningAsync(request);
        await flow.FinishTurnAsync(job);
        await flow.Check.HandleAsync(new TurnWoken(Outcomes.Present(job.Session), TurnId.New()), Cancellation);
        await flow.SettledAsync(job);

        return job;
    }
}
