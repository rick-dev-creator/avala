using System.Collections.Concurrent;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Verification.Tests.Verifying;

internal sealed class ScriptedProcesses(FakeTimeProvider clock) : IProcessRunner
{
    private readonly Dictionary<string, Script> scripts = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<ProcessRequest> requests = new();

    public IReadOnlyList<ProcessRequest> Requests => [.. requests];

    public ScriptedProcesses Exits(string command, int exitCode, TimeSpan duration, string output = "", string error = "")
    {
        scripts[command] = new Script(new ProcessOutcome(exitCode, output, error), duration, Hangs: false);

        return this;
    }

    public ScriptedProcesses Hangs(string command, TimeSpan advance)
    {
        scripts[command] = new Script(new ProcessOutcome(0, string.Empty, string.Empty), advance, Hangs: true);

        return this;
    }

    public async ValueTask<Result<ProcessOutcome, ProcessError>> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
    {
        requests.Enqueue(request);

        if (!scripts.TryGetValue(request.FileName, out var script))
        {
            return ProcessError.NotFound;
        }

        clock.Advance(script.Duration);

        if (script.Hangs)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        return script.Outcome;
    }

    private sealed record Script(ProcessOutcome Outcome, TimeSpan Duration, bool Hangs);
}
