using Avala.Jobs.Contracts;
using Avala.Jobs.Holding;
using Avala.Jobs.Jobs;
using Avala.Jobs.Ledger;
using Avala.Sdk;
using Microsoft.Extensions.Logging;

namespace Avala.Jobs.Launching;

internal sealed partial class StartJob(JobLedger ledger, JobLauncher launcher, HoldJob hold, ILogger<StartJob> logger)
{
    public async Task ExecuteAsync(Job job, CancellationToken cancellationToken)
    {
        Option<string> problem;

        try
        {
            problem = await launcher.LaunchAsync(job, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogLaunchThrew(job.Id.Value, exception);
            problem = exception.Message;
        }

        await problem.Match(found => HoldAsync(job, found, cancellationToken), () => Task.CompletedTask);
    }

    public async Task<Result<JobId, JobRejection>> RetryAsync(Job job, CancellationToken cancellationToken)
    {
        if (job.RetryLaunch().IsFailure)
        {
            return JobRejection.NotHeld;
        }

        await ledger.RecordAsync(job, cancellationToken);
        await ExecuteAsync(job, cancellationToken);

        return job.Id;
    }

    private async Task HoldAsync(Job job, string problem, CancellationToken cancellationToken)
    {
        LogCouldNotStart(job.Id.Value, problem);
        await hold.CouldNotStartAsync(job, problem, cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Launching job {Job} threw")]
    private partial void LogLaunchThrew(Guid job, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Job {Job} could not start and waits for a person: {Problem}")]
    private partial void LogCouldNotStart(Guid job, string problem);
}
