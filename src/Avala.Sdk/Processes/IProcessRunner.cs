namespace Avala.Sdk.Processes;

public interface IProcessRunner
{
    ValueTask<Result<ProcessOutcome, ProcessError>> RunAsync(ProcessRequest request, CancellationToken cancellationToken);
}
