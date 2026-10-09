using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Jobs;
using Avala.Sdk;

namespace Avala.Jobs.Launching;

internal sealed record Round(
    Func<Job, Feedback, Result<AttemptStarted, JobError>> InSameSession,
    Func<Job, Feedback, SessionId, bool, Result<AttemptStarted, JobError>> InNewSession)
{
    public static Round Hint { get; } = new(
        (job, guidance) => job.Hint(guidance),
        (job, guidance, session, resumed) => job.Hint(guidance, session, resumed));

    public static Round SendBack { get; } = new(
        (job, feedback) => job.SendBack(feedback),
        (job, feedback, session, resumed) => job.SendBack(feedback, session, resumed));
}
