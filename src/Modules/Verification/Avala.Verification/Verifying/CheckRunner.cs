using Avala.Sdk.Processes;
using Avala.Verification.Checks;
using Avala.Verification.Contracts;

namespace Avala.Verification.Verifying;

internal sealed class CheckRunner(IProcessRunner processes, TimeProvider clock)
{
    public async Task<CheckEvidence> RunAsync(DeclaredCheck check, string workingDirectory, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(check.Timeout, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var started = clock.GetTimestamp();

        try
        {
            var result = await processes.RunAsync(new ProcessRequest(check.Command, check.Arguments, workingDirectory), linked.Token);
            var duration = clock.GetElapsedTime(started);

            return result.Match(
                outcome => check.Exited(outcome.ExitCode, outcome.Output, outcome.Error, duration),
                _ => check.NotFound(duration));
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return check.TimedOut(clock.GetElapsedTime(started));
        }
    }
}
