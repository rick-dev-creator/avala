using System.Runtime.CompilerServices;
using Avala.Agents.Contracts.Events;
using Avala.Simulator.Scenarios;

namespace Avala.Simulator.Playback;

internal sealed class Performer(string workingDirectory, IFileWriter files, PermissionGate permissions)
{
    public async IAsyncEnumerable<IAgentEvent> PlayAsync(
        Cues cues,
        IReadOnlyList<IStep> script,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return cues.Started;

        foreach (var step in script)
        {
            await foreach (var cue in PlayAsync(cues, step, cancellationToken))
            {
                yield return cue;

                if (cue is TurnCompleted)
                {
                    yield break;
                }
            }
        }
    }

    private async IAsyncEnumerable<IAgentEvent> PlayAsync(Cues cues, IStep step, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        switch (step)
        {
            case WriteFile write:
                yield return cues.Opened(write.Item, ItemKind.FileEdit, $"Edit {write.Path}");
                await files.WriteAsync(Path.Combine(workingDirectory, write.Path), write.Content, cancellationToken);
                yield return cues.Progressed(write.Item, write.Content);
                yield return cues.Closed(write.Item, ItemOutcome.Succeeded);
                break;
            case RunCommand run:
                await foreach (var cue in RunAsync(cues, run, cancellationToken))
                {
                    yield return cue;
                }

                break;
            case Crash crash:
                throw new InvalidOperationException(crash.Reason);
            default:
                foreach (var cue in cues.Of(step))
                {
                    yield return cue;
                }

                break;
        }
    }

    private async IAsyncEnumerable<IAgentEvent> RunAsync(Cues cues, RunCommand run, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return cues.Opened(run.Item, ItemKind.Command, run.Command);

        if (run.AsksPermission)
        {
            var decision = permissions.ExpectAsync(run.Item);
            yield return cues.Asked(run.Item, $"Run {run.Command}", ItemKind.Command, run.Command);

            var answer = await AwaitAsync(run, decision, cancellationToken);
            yield return cues.Answered(run.Item, answer);

            if (answer == PermissionAnswer.Deny)
            {
                yield return cues.Closed(run.Item, ItemOutcome.Cancelled);
                yield return cues.Ended(TurnOutcome.Finished);
                yield break;
            }
        }

        yield return cues.Progressed(run.Item, run.Output);
        yield return cues.Closed(run.Item, ItemOutcome.Succeeded);
    }

    private async Task<PermissionAnswer> AwaitAsync(RunCommand run, Task<PermissionAnswer> decision, CancellationToken cancellationToken)
    {
        try
        {
            return await decision.WaitAsync(cancellationToken);
        }
        finally
        {
            permissions.Withdraw(run.Item);
        }
    }
}
