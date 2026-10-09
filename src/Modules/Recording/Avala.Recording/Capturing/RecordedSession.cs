using System.Runtime.CompilerServices;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Recording.Recordings;
using Avala.Sdk;

namespace Avala.Recording.Capturing;

internal sealed class RecordedSession(IAgentSession inner, string workingDirectory, Journal journal, IEditedFiles files) : IAgentSession
{
    private bool stopped;

    public SessionId Id => inner.Id;

    public Option<AgentAccount> Account => inner.Account;

    public IAsyncEnumerable<IAgentEvent> Events => RecordAsync(CancellationToken.None);

    public ValueTask<Result<TurnId, AgentError>> SendAsync(UserTurn turn, CancellationToken cancellationToken) =>
        InputAsync(sequence => new Sent(sequence, turn), () => inner.SendAsync(turn, cancellationToken));

    public ValueTask<Result<ItemId, AgentError>> RespondAsync(PermissionDecision decision, CancellationToken cancellationToken) =>
        InputAsync(sequence => new Responded(sequence, decision), () => inner.RespondAsync(decision, cancellationToken));

    public ValueTask<Result<ItemId, AgentError>> AnswerAsync(FormAnswer answer, CancellationToken cancellationToken) =>
        InputAsync(sequence => new Answered(sequence, answer), () => inner.AnswerAsync(answer, cancellationToken));

    public ValueTask<Result<ItemId, AgentError>> ReturnAsync(ToolResult result, CancellationToken cancellationToken) =>
        InputAsync(sequence => new Returned(sequence, result), () => inner.ReturnAsync(result, cancellationToken));

    public ValueTask<Result<TurnId, AgentError>> InterruptAsync(CancellationToken cancellationToken) =>
        InputAsync(sequence => new Interrupted(sequence), () => inner.InterruptAsync(cancellationToken));

    public async ValueTask DisposeAsync()
    {
        Volatile.Write(ref stopped, true);
        journal.Note(new Stopped());
        await inner.DisposeAsync();
    }

    private async ValueTask<Result<T, AgentError>> InputAsync<T>(Func<int, IHarnessInput> input, Func<ValueTask<Result<T, AgentError>>> call)
    {
        var sequence = journal.NextInput();
        journal.Note(input(sequence));
        var result = await call();

        if (!result.TryGetValue(out _, out var error))
        {
            journal.Note(new Refused(sequence, error));
        }

        return result;
    }

    private async IAsyncEnumerable<IAgentEvent> RecordAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var edits = new Dictionary<ItemId, string>();
        await using var events = inner.Events.GetAsyncEnumerator(cancellationToken);

        while (await NextAsync(events, cancellationToken))
        {
            var agentEvent = events.Current;

            if (agentEvent is PermissionRequested { Kind: ItemKind.FileEdit } requested && !requested.Target.Contains('\0', StringComparison.Ordinal))
            {
                edits[requested.Item] = requested.Target;
            }

            if (agentEvent is ItemCompleted completed && edits.Remove(completed.Item, out var target) && completed.Outcome == ItemOutcome.Succeeded)
            {
                await CaptureAsync(completed.Item, target, cancellationToken);
            }

            journal.Note(new Observed(agentEvent));

            yield return agentEvent;
        }
    }

    private async Task<bool> NextAsync(IAsyncEnumerator<IAgentEvent> events, CancellationToken cancellationToken)
    {
        try
        {
            var more = await events.MoveNextAsync();

            if (!more && OnItsOwn(cancellationToken))
            {
                journal.Note(new StreamEnded(Crashed: false));
            }

            return more;
        }
        catch (Exception) when (OnItsOwn(cancellationToken))
        {
            journal.Note(new StreamEnded(Crashed: true));
            throw;
        }
    }

    private bool OnItsOwn(CancellationToken cancellationToken) => !cancellationToken.IsCancellationRequested && !Volatile.Read(ref stopped);

    private async Task CaptureAsync(ItemId item, string target, CancellationToken cancellationToken)
    {
        var path = Path.GetFullPath(target, workingDirectory);

        foreach (var content in (await files.ReadAsync(path, cancellationToken)).Match<string[]>(text => [text], () => []))
        {
            journal.Note(new FileCaptured(item, Path.GetRelativePath(workingDirectory, path).Replace('\\', '/'), content));
        }
    }
}
