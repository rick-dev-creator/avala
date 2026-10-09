using System.Runtime.CompilerServices;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Simulator.Scenarios;

namespace Avala.Simulator.Playback;

internal sealed class Performer(SessionOptions options, Stagecraft craft, Gates gates)
{
    private readonly Replayer replayer = new(options, craft.Files, gates, craft.Pacing);

    public bool Closing => replayer.Closing;

    public bool Expects(Conversation conversation) => !conversation.Scenario.Recorded || replayer.AwaitsInterrupt;

    public IAsyncEnumerable<IAgentEvent> PlayAsync(Cues cues, Conversation conversation, CancellationToken cancellationToken) =>
        conversation.Scenario.Recorded
            ? replayer.PlayAsync(cues, conversation, cancellationToken)
            : PerformAsync(cues, conversation, cancellationToken);

    private async IAsyncEnumerable<IAgentEvent> PerformAsync(
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
            new Deed(write.Item, ItemKind.FileEdit, $"Edit {write.Path}", $"Edit {write.Path}", Path.Combine(options.WorkingDirectory, write.Path)),
            options.Permissions == PermissionMode.AskEveryTime,
            async token =>
            {
                await craft.Files.WriteAsync(Path.Combine(options.WorkingDirectory, write.Path), write.Content, token);

                return write.Content;
            },
            cancellationToken),
        RunCommand run => ActAsync(
            cues,
            new Deed(run.Item, ItemKind.Command, run.Command, $"Run {run.Command}", run.Command),
            options.Permissions == PermissionMode.AskEveryTime || (options.Permissions == PermissionMode.AllowEdits && run.AsksPermission),
            _ => Task.FromResult(run.Output),
            cancellationToken),
        Spawn spawn => ActAsync(
            cues,
            new Deed(spawn.Item, ItemKind.Command, spawn.Command, $"Run {spawn.Command}", spawn.Command),
            options.Permissions != PermissionMode.AllowAll,
            token => craft.Workloads.StartAsync(options.Processes, spawn.Workload, options.WorkingDirectory, token),
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
        Func<CancellationToken, Task<string>> perform,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return cues.Opened(deed.Item, deed.Kind, deed.Title);

        if (asks)
        {
            var pending = await gates.Permissions.ExpectAsync(deed.Item, cancellationToken);
            yield return cues.Asked(deed.Item, deed.Request, deed.Kind, deed.Target);

            var decision = await gates.Permissions.AwaitAsync(deed.Item, pending, cancellationToken);
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

        yield return cues.Progressed(deed.Item, await perform(cancellationToken));
        yield return cues.Closed(deed.Item, ItemOutcome.Succeeded);
    }

    private async IAsyncEnumerable<IAgentEvent> AskAsync(Cues cues, Ask ask, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var pending = await gates.Forms.ExpectAsync(ask.Item, cancellationToken);
        yield return cues.Asked(ask.Item, ask.Form);

        var answer = await gates.Forms.AwaitAsync(ask.Item, pending, cancellationToken);
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

    private sealed record Deed(ItemId Item, ItemKind Kind, string Title, string Request, string Target);
}
