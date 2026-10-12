using Avala.Agents.Contracts.Connections;
using Avala.Handoffs.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Conversation;
using Avala.Workbench.NewJob;
using Avala.Workbench.Steering;

namespace Avala.Workbench.Tests;

internal static class Composing
{
    public static Composers Of(JobSteering steering, SendKeys keys) => new(steering, keys, Harnesses(new FakeJobs(), new FakeHandoffRequests(), new FakeConnections()));

    public static JobHarnesses Harnesses(IJobs jobs, IHandoffRequests handoffs, IConnections connections) =>
        new(new JobSwitching(jobs, handoffs, connections), new NewJobReadings(Pages.Readings(new FakeUsage())));

    public static JobHarness Harness(JobId job) => Harnesses(new FakeJobs(), new FakeHandoffRequests(), new FakeConnections()).Open(job);
}

internal sealed class FakeHandoffRequests : IHandoffRequests
{
    public List<(JobId Job, ConnectionName To)> Requests { get; } = [];

    public Option<JobRejection> Refusal { get; set; }

    public ValueTask<Result<JobContinuation, JobRejection>> HandOffAsync(JobId job, ConnectionName to, CancellationToken cancellationToken)
    {
        Requests.Add((job, to));

        return ValueTask.FromResult(Refusal.Match(
            rejection => Result<JobContinuation, JobRejection>.Failure(rejection),
            () => Result<JobContinuation, JobRejection>.Success(new JobContinuation(job, Avala.Agents.Contracts.Sessions.SessionId.New(), ContinuedIn.NewConversation))));
    }
}
