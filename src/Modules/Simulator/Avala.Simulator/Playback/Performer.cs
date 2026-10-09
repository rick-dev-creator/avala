using System.Runtime.CompilerServices;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Simulator.Scenarios;

namespace Avala.Simulator.Playback;

internal sealed class Performer(SessionOptions options, IFileWriter files, PermissionGate permissions)
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

    private IAsyncEnumerable<IAgentEvent> PlayAsync(Cues cues, IStep step, CancellationToken cancellationToken) => step switch
    {
        WriteFile write => ActAsync(
            cues,
            new Deed(write.Item, ItemKind.FileEdit, $"Edit {write.Path}", $"Edit {write.Path}", Path.Combine(options.WorkingDirectory, write.Path), write.Content),
            options.Permissions == PermissionMode.AskEveryTime,
            token => files.WriteAsync(Path.Combine(options.WorkingDirectory, write.Path), write.Content, token).AsTask(),
            cancellationToken),
        RunCommand run => ActAsync(
            cues,
            new Deed(run.Item, ItemKind.Command, run.Command, $"Run {run.Command}", run.Command, run.Output),
            options.Permissions == PermissionMode.AskEveryTime || (options.Permissions == PermissionMode.AllowEdits && run.AsksPermission),
            _ => Task.CompletedTask,
            cancellationToken),
        Crash crash => throw new InvalidOperationException(crash.Reason),
        _ => cues.Of(step).ToAsyncEnumerable(),
    };

    private async IAsyncEnumerable<IAgentEvent> ActAsync(
        Cues cues,
        Deed deed,
        bool asks,
        Func<CancellationToken, Task> perform,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return cues.Opened(deed.Item, deed.Kind, deed.Title);

        if (asks)
        {
            var decision = await permissions.ExpectAsync(deed.Item, cancellationToken);
            yield return cues.Asked(deed.Item, deed.Request, deed.Kind, deed.Target);

            var answer = await AwaitAsync(deed.Item, decision, cancellationToken);
            yield return cues.Answered(deed.Item, answer);

            if (answer == PermissionAnswer.Deny)
            {
                yield return cues.Closed(deed.Item, ItemOutcome.Cancelled);
                yield return cues.Ended(TurnOutcome.Finished);
                yield break;
            }
        }

        await perform(cancellationToken);
        yield return cues.Progressed(deed.Item, deed.Output);
        yield return cues.Closed(deed.Item, ItemOutcome.Succeeded);
    }

    private async Task<PermissionAnswer> AwaitAsync(ItemId item, Task<PermissionAnswer> decision, CancellationToken cancellationToken)
    {
        try
        {
            return await decision.WaitAsync(cancellationToken);
        }
        finally
        {
            await permissions.WithdrawAsync(item);
        }
    }

    private sealed record Deed(ItemId Item, ItemKind Kind, string Title, string Request, string Target, string Output);
}
