using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Recording.Recordings;

namespace Avala.Recording.Capturing;

internal sealed class FileCapture(string workingDirectory, Journal journal, IEditedFiles files)
{
    private readonly Dictionary<ItemId, string> edits = [];
    private readonly Dictionary<ItemId, (string Line, FolderStamps Before)> commands = [];

    public async Task ObserveAsync(IAgentEvent agentEvent, CancellationToken cancellationToken)
    {
        switch (agentEvent)
        {
            case PermissionRequested { Kind: ItemKind.FileEdit } requested when !requested.Target.Contains('\0', StringComparison.Ordinal):
                edits[requested.Item] = requested.Target;
                break;
            case ItemStarted { Kind: ItemKind.Command } command:
                commands[command.Item] = (command.Input.Match(input => input, () => command.Title), await files.StampAsync(workingDirectory, cancellationToken));
                break;
            case ItemCompleted completed:
                await CompletedAsync(completed, cancellationToken);
                break;
        }
    }

    private async Task CompletedAsync(ItemCompleted completed, CancellationToken cancellationToken)
    {
        var edited = edits.Remove(completed.Item, out var target);
        var ran = commands.Remove(completed.Item, out var run);

        if (completed.Outcome != ItemOutcome.Succeeded)
        {
            return;
        }

        if (edited)
        {
            await CaptureAsync(completed.Item, target!, cancellationToken);
        }

        if (ran)
        {
            foreach (var written in (await files.StampAsync(workingDirectory, cancellationToken)).ChangedSince(run.Before).Where(path => Names(run.Line, path)))
            {
                await CaptureAsync(completed.Item, written, cancellationToken);
            }
        }
    }

    private static bool Names(string command, string path) => command.Contains(Path.GetFileName(path), StringComparison.Ordinal);

    private async Task CaptureAsync(ItemId item, string target, CancellationToken cancellationToken)
    {
        var path = Path.GetFullPath(target, workingDirectory);

        foreach (var content in (await files.ReadAsync(path, cancellationToken)).Match<string[]>(text => [text], () => []))
        {
            journal.Note(new FileCaptured(item, Path.GetRelativePath(workingDirectory, path).Replace('\\', '/'), content));
        }
    }
}
