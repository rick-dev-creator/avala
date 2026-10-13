using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Microsoft.Extensions.Logging;

namespace Avala.Jobs.Holding;

internal sealed partial class JobJournal(ILogger<JobJournal> logger)
{
    public void Held(JobHold hold) => LogHeld(hold.Job.Value, hold.Reason, hold.Halt);

    public void Unconfirmed(JobId job, SessionId session, Option<ConnectionName> connection) =>
        LogUnconfirmed(job.Value, connection.Match(name => name.Value, () => "no connection"), session.Value);

    public Result<T, JobRejection> Delivered<T>(JobId job, Result<T, JobRejection> outcome)
        where T : notnull
    {
        if (!outcome.TryGetValue(out _, out var rejection))
        {
            LogRefused(job.Value, rejection);
        }

        return outcome;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Job {Job} is held as {Reason}, its session {Halt}")]
    private partial void LogHeld(Guid job, HoldReason reason, SessionHalt halt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Job {Job} on {Connection} never confirmed the interrupt of its session {Session}, so the session is stopped")]
    private partial void LogUnconfirmed(Guid job, string connection, Guid session);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A message to job {Job} was refused: {Rejection}")]
    private partial void LogRefused(Guid job, JobRejection rejection);
}
