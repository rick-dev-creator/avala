using System.Runtime.CompilerServices;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Simulator.Scenarios;

namespace Avala.Simulator.Playback;

internal sealed class Performer(
    SessionOptions options,
    IFileWriter files,
    ReplyGate<PermissionDecision> permissions,
    ReplyGate<FormAnswer> forms)
{
    public async IAsyncEnumerable<IAgentEvent> PlayAsync(
        Cues cues,
        Conversation conversation,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var script = conversation.NextScript;
        yield return cues.Started;
        yield return cues.Resumable(conversation.Advanced.Token);

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
        Ask ask => AskAsync(cues, ask, cancellationToken),
        Crash crash => throw new InvalidOperationException(crash.Reason),
        Draw draw when !options.Tools.Any(tool => tool.Surface == ToolSurface.Canvas) =>
            cues.Of(new Say(draw.Item, ItemKind.Message, draw.Title, draw.Chunks)).ToAsyncEnumerable(),
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
            var pending = await permissions.ExpectAsync(deed.Item, cancellationToken);
            yield return cues.Asked(deed.Item, deed.Request, deed.Kind, deed.Target);

            var decision = await AwaitAsync(permissions, deed.Item, pending, cancellationToken);
            yield return cues.Answered(deed.Item, decision.Answer);

            if (decision.Answer == PermissionAnswer.Deny)
            {
                yield return cues.Closed(deed.Item, ItemOutcome.Cancelled);

                foreach (var cue in decision.Message.Match(message => cues.Of(Reply(deed.Item, $"Understood, I will not go on: {message}")), () => []))
                {
                    yield return cue;
                }

                yield return cues.Ended(TurnOutcome.Finished);
                yield break;
            }
        }

        await perform(cancellationToken);
        yield return cues.Progressed(deed.Item, deed.Output);
        yield return cues.Closed(deed.Item, ItemOutcome.Succeeded);
    }

    private async IAsyncEnumerable<IAgentEvent> AskAsync(Cues cues, Ask ask, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var pending = await forms.ExpectAsync(ask.Item, cancellationToken);
        yield return cues.Asked(ask.Item, ask.Form);

        var answer = await AwaitAsync(forms, ask.Item, pending, cancellationToken);
        var goesOn = !answer.Declined && answer.Fields.All(given => given.Confirmed || ask.Form.Fields.Any(field => field.Id == given.Field && field.Kind != FieldKind.Confirmation));
        yield return cues.Answered(ask.Item, answer);
        yield return cues.Closed(ask.Item, answer.Declined ? ItemOutcome.Cancelled : ItemOutcome.Succeeded);

        foreach (var cue in cues.Of(Reply(ask.Item, Echo(ask.Form, answer))))
        {
            yield return cue;
        }

        if (!goesOn)
        {
            yield return cues.Ended(TurnOutcome.Finished);
        }
    }

    private static Say Reply(ItemId item, string text) => new(new ItemId($"{item.Value}-reply"), ItemKind.Message, "Reply", [text]);

    private static string Echo(AgentForm form, FormAnswer answer) =>
        answer.Declined
            ? answer.Message.Match(message => $"Understood, I will not go on: {message}", () => "Understood, I will not go on.")
            : "Going with " + string.Join("; ", answer.Fields.Select(given =>
                $"{form.Fields.First(field => field.Id == given.Field).Header}: {Value(form.Fields.First(field => field.Id == given.Field), given)}"));

    private static string Value(FormField field, FieldAnswer given) =>
        field.Kind == FieldKind.Confirmation
            ? (given.Confirmed ? "approved" : "not approved") + given.Text.Match(text => $" ({text})", () => string.Empty)
            : string.Join(", ", [.. given.Chosen, .. given.Text.Match<string[]>(text => [text], () => [])]);

    private static async Task<TReply> AwaitAsync<TReply>(ReplyGate<TReply> gate, ItemId item, Task<TReply> reply, CancellationToken cancellationToken)
    {
        try
        {
            return await reply.WaitAsync(cancellationToken);
        }
        finally
        {
            await gate.WithdrawAsync(item);
        }
    }

    private sealed record Deed(ItemId Item, ItemKind Kind, string Title, string Request, string Target, string Output);
}
