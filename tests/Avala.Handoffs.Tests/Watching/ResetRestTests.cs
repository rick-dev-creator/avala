using Avala.Agents.Contracts.Connections;
using Avala.Handoffs.Contracts;
using Avala.Handoffs.Watching;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Handoffs.Tests.Watching;

public sealed class ResetRestTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public async Task OnlyAJobWaitingForAPersonPastItsResetHasItsIdleSessionRestedAsync(bool hasReset, bool awaitsPerson, bool rested)
    {
        var sessions = new RestedSessions();
        var wait = new ResetWait(JobId.New(), new ConnectionName("personal"), "fiveHours", Option<DateTimeOffset>.None, DateTimeOffset.UnixEpoch) { HasReset = hasReset, AwaitsPerson = awaitsPerson };

        await new ResetRest(sessions).HandleAsync(new JobWaitsForReset(wait), Cancellation);

        Assert.Equal(rested ? [wait.Job] : [], sessions.Rested);
    }

    private sealed class RestedSessions : IJobSessions
    {
        public List<JobId> Rested { get; } = [];

        public ValueTask<Result<JobId, JobRejection>> RestAsync(JobId job, CancellationToken cancellationToken)
        {
            Rested.Add(job);

            return ValueTask.FromResult(Result<JobId, JobRejection>.Success(job));
        }
    }
}
