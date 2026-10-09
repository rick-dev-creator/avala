using System.Runtime.CompilerServices;
using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Simulator.Scenarios;

namespace Avala.Simulator.Playback;

internal sealed class Performer(SessionOptions options, Stagecraft craft, Gates gates, CapabilitySet declared)
{
    private readonly Replayer replayer = new(options, craft.Files, gates, craft.Pacing);
    private int acknowledged;

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
        Ask ask when !declared.Has<AsksForms>() =>
            cues.Of(Reply(ask.Item, $"I would ask: {ask.Form.Title}, but the harness takes no forms, so I stop here."))
                .Append(cues.Ended(TurnOutcome.Finished))
                .ToAsyncEnumerable(),
        Ask ask => AskAsync(cues, ask, cancellationToken),
        AwaitMessage when !declared.Has<AcceptsMessagesMidTurn>() => AsyncEnumerable.Empty<IAgentEvent>(),
        AwaitMessage => HearAsync(cues, cancellationToken),
        Finish => Heard(cues).Append(cues.Ended(TurnOutcome.Finished)).ToAsyncEnumerable(),
        CallTool call => PlayAsync(cues, new CallTools([call]), cancellationToken),
        CallTools calls when calls.Calls.All(call => options.Tools.Any(tool => tool.Name == call.Tool && tool.Surface == ToolSurface.Executed)) =>
            CallAsync(cues, calls.Calls, cancellationToken),
        CallTools calls => calls.Calls
            .SelectMany(call => cues.Of(Reply(call.Item, $"I would call {call.Tool} with {call.Input}, but the harness did not offer it.")))
            .ToAsyncEnumerable(),
        ReportLimitResetting limit => cues.Of(new ReportLimit(new UsageLimit(limit.Window, limit.Used, craft.Pacing.Now + limit.ResetsIn))).ToAsyncEnumerable(),
        Crash crash => throw new InvalidOperationException(crash.Reason),
        Draw draw when !options.Tools.Any(tool => tool.Surface == ToolSurface.Canvas) =>
            cues.Of(new Say(draw.Item, ItemKind.Message, draw.Title, draw.Chunks)).ToAsyncEnumerable(),
        _ => DeedAsync(cues, step, cancellationToken),
    };

    private IAsyncEnumerable<IAgentEvent> DeedAsync(Cues cues, IStep step, CancellationToken cancellationToken) => step switch
    {
        WriteFile write => ActAsync(
            cues,
            new Deed(write.Item, ItemKind.FileEdit, $"Edit {write.Path}", $"Edit {write.Path}", Path.Combine(options.WorkingDirectory, write.Path), write.Content),
            options.Permissions == PermissionMode.AskEveryTime,
            async token =>
            {
                await craft.Files.WriteAsync(Path.Combine(options.WorkingDirectory, write.Path), write.Content, token);

                return write.Content;
            },
            cancellationToken),
        RunCommand run => ActAsync(
            cues,
            new Deed(run.Item, ItemKind.Command, run.Command, $"Run {run.Command}", run.Command, run.Command),
            options.Permissions == PermissionMode.AskEveryTime || (options.Permissions == PermissionMode.AllowEdits && run.AsksPermission),
            _ => Task.FromResult(run.Output),
            cancellationToken),
        WriteThroughCommand write => ActAsync(
            cues,
            new Deed(write.Item, ItemKind.Command, FirstLine(write.Command), $"Run {FirstLine(write.Command)}", write.Command, write.Command),
            options.Permissions != PermissionMode.AllowAll,
            async token =>
            {
                await craft.Files.WriteAsync(Path.Combine(options.WorkingDirectory, write.Path), write.Content, token);

                return string.Empty;
            },
            cancellationToken),
        UseTool use => ActAsync(
            cues,
            new Deed(use.Item, use.Kind, use.Title, use.Title, use.Target, use.Input),
            use.AsksPermission && options.Permissions != PermissionMode.AllowAll,
            _ => Task.FromResult(use.Output),
            cancellationToken),
        Spawn spawn => ActAsync(
            cues,
            new Deed(spawn.Item, ItemKind.Command, spawn.Command, $"Run {spawn.Command}", spawn.Command, spawn.Command),
            options.Permissions != PermissionMode.AllowAll,
            token => craft.Workloads.StartAsync(options.Processes, spawn.Workload, options.WorkingDirectory, token),
            cancellationToken),
        _ => cues.Of(step).ToAsyncEnumerable(),
    };

    private async IAsyncEnumerable<IAgentEvent> ActAsync(
        Cues cues,
        Deed deed,
        bool asks,
        Func<CancellationToken, Task<string>> perform,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return cues.Opened(deed.Item, deed.Kind, deed.Title, deed.Input);

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

        var output = await perform(cancellationToken);

        if (output.Length > 0)
        {
            yield return cues.Progressed(deed.Item, output);
        }

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

    private async IAsyncEnumerable<IAgentEvent> CallAsync(
        Cues cues,
        IReadOnlyList<CallTool> calls,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var waiting = new List<Task<ToolResult>>();

        foreach (var call in calls)
        {
            var pending = await gates.Tools.ExpectAsync(call.Item, cancellationToken);
            waiting.Add(gates.Tools.AwaitAsync(call.Item, pending, cancellationToken));
            yield return cues.Called(call.Item, call.Tool, call.Input);
        }

        var answers = new List<string>();

        while (waiting.Count > 0)
        {
            var answered = await Task.WhenAny(waiting);
            waiting.Remove(answered);
            var result = await answered;
            answers.Add(result.Content);
            yield return cues.Returned(result.Item, result);
            yield return cues.Closed(result.Item, result.IsError ? ItemOutcome.Failed : ItemOutcome.Succeeded);
        }

        foreach (var cue in cues.Of(Reply(calls[0].Item, $"The harness answered: {string.Join(' ', answers)}")))
        {
            yield return cue;
        }
    }

    private async IAsyncEnumerable<IAgentEvent> HearAsync(Cues cues, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var cue in Acknowledged(cues, await gates.Messages.Reader.ReadAsync(cancellationToken)))
        {
            yield return cue;
        }
    }

    private List<IAgentEvent> Heard(Cues cues)
    {
        var heard = new List<IAgentEvent>();

        while (gates.Messages.Reader.TryRead(out var message))
        {
            heard.AddRange(Acknowledged(cues, message));
        }

        return heard;
    }

    private IEnumerable<IAgentEvent> Acknowledged(Cues cues, string message) =>
        cues.Of(new Say(new ItemId($"heard-{++acknowledged}"), ItemKind.Message, "Reply", [$"Noted: {message} ", "I am folding it into this turn."]));

    private static string FirstLine(string text) => text.Split('\n', 2)[0];

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

    private sealed record Deed(ItemId Item, ItemKind Kind, string Title, string Request, string Target, string Input);
}
