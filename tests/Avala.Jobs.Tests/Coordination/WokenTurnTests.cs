using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Jobs.Jobs;
using Avala.Jobs.TurnChecks;
using Avala.Testing;

namespace Avala.Jobs.Tests.Coordination;

public sealed class WokenTurnTests
{
    private static CancellationToken Cancellation => JobFlow.Cancellation;

    [Fact]
    public async Task AnAgentWokenByItsBackgroundWorkWhileItsJobAwaitsReviewStartsAvalasWokenRoundAsync()
    {
        var flow = JobFlow.With();
        var job = await flow.RunningAsync();
        await flow.FinishTurnAsync(job);

        await flow.Check.HandleAsync(new TurnWoken(Outcomes.Present(job.Session), TurnId.New()), Cancellation);
        await flow.SettledAsync(job);

        Assert.Equal(JobState.Running, job.State);
        Assert.Equal((AttemptOrigin.Woken, MessageAuthor.Avala), (job.Attempts[^1].Origin, job.Attempts[^1].Cause));
        Assert.Equal(CheckTurn.WokenNote, Outcomes.Present(job.Attempts[^1].Guidance).Text);
    }

    [Fact]
    public async Task AnAgentWokenWhileItsJobIsHeldIsInterruptedAgainAndStaysHeldAsync()
    {
        var flow = JobFlow.With();
        var job = await flow.HeldAsync(HoldReason.Stalled);
        var session = Outcomes.Present(job.Session);

        await flow.Check.HandleAsync(new TurnWoken(session, TurnId.New()), Cancellation);
        await flow.SettledAsync(job);

        Assert.Equal(JobState.NeedsHelp, job.State);
        Assert.Equal([session, session], flow.Agents.Interrupted);
    }
}
