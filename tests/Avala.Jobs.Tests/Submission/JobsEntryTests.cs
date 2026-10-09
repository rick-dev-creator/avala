using Avala.Jobs.Contracts;
using Avala.Jobs.Tests.Coordination;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Jobs.Tests.Submission;

public sealed class JobsEntryTests
{
    private static CancellationToken Cancellation => JobFlow.Cancellation;

    [Fact]
    public async Task AnAcceptedRequestReturnsTheIdOfTheSubmittedJobAsync()
    {
        var flow = JobFlow.With();

        var id = Outcomes.Succeeds(await flow.Jobs.SubmitAsync(new JobRequest("/repos/shop", "Add GitHub login") { Autonomy = Autonomy.Supervised }, Cancellation));

        var job = Assert.Single(flow.Store.Jobs);
        Assert.Equal((id, Option<Autonomy>.Some(Autonomy.Supervised)), (job.Id, job.Autonomy));
    }

    [Theory]
    [InlineData(" ", "Add GitHub login", 3, JobRejection.EmptyRepository)]
    [InlineData("/repos/shop", " ", 3, JobRejection.EmptyInstruction)]
    [InlineData("/repos/shop", "Add GitHub login", 0, JobRejection.InvalidAttemptBudget)]
    public async Task AnInvalidFieldRejectsTheRequestWithItsReasonAsync(string repository, string instruction, int attempts, JobRejection expected)
    {
        var flow = JobFlow.With();

        var rejection = Outcomes.FailsWith(await flow.Jobs.SubmitAsync(new JobRequest(repository, instruction, attempts), Cancellation));

        Assert.Equal(expected, rejection);
        Assert.Empty(flow.Store.Jobs);
    }
}
