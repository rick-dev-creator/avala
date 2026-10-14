using Avala.Sdk;
using Avala.Sdk.Processes;

namespace Avala.Testing;

public sealed class ProcessesInFlight(TimeProvider clock)
{
    private int running;
    private long touched;

    public ProcessesInFlight()
        : this(TimeProvider.System)
    {
    }

    public static ProcessesInFlight None { get; } = new();

    public bool Running => Volatile.Read(ref running) > 0;

    public TimeSpan Quiet => Interlocked.Read(ref touched) is var last && last == 0 ? TimeSpan.MaxValue : clock.GetElapsedTime(last);

    public IProcessRunner Counting(IProcessRunner runner) => new CountedRunner(this, runner);

    private async ValueTask<Result<ProcessOutcome, ProcessError>> RunAsync(IProcessRunner runner, ProcessRequest request, CancellationToken cancellationToken)
    {
        _ = Interlocked.Increment(ref running);
        Touch();

        try
        {
            return await runner.RunAsync(request, cancellationToken);
        }
        finally
        {
            _ = Interlocked.Decrement(ref running);
            Touch();
        }
    }

    private void Touch() => Interlocked.Exchange(ref touched, clock.GetTimestamp());

    private sealed class CountedRunner(ProcessesInFlight processes, IProcessRunner runner) : IProcessRunner
    {
        public ValueTask<Result<ProcessOutcome, ProcessError>> RunAsync(ProcessRequest request, CancellationToken cancellationToken) =>
            processes.RunAsync(runner, request, cancellationToken);
    }
}
