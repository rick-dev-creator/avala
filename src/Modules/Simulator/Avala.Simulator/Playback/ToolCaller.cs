using System.Runtime.CompilerServices;
using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;
using Avala.Simulator.Scenarios;

namespace Avala.Simulator.Playback;

internal sealed class ToolCaller(SessionOptions options, Gates gates, CapabilitySet declared)
{
    private int called;

    public async IAsyncEnumerable<IAgentEvent> CallAsync(Cues cues, CallTools step, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var round = new Round(cues, step.AnswersChildren.Bind(chosen => Offers(ScenarioCatalog.AnswerChild) ? chosen : Option<string>.None));

        foreach (var call in step.Calls)
        {
            round.Waiting.Add(await PendingAsync(call.Item, cancellationToken));
            yield return cues.Called(call.Item, call.Tool, call.Input);
        }

        var listening = declared.Has<AcceptsMessagesMidTurn>() ? round.Answering : Option<string>.None;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var heard = ListenAsync(listening, stop.Token);

        while (round.Waiting.Count > 0)
        {
            var next = await Task.WhenAny(round.Waiting.Cast<Task>().Append(heard));
            var noted = next == heard;
            var events = noted ? NotedAsync(cues, await heard, listening, cancellationToken) : ReturnedAsync(round, (Task<ToolResult>)next, cancellationToken);

            await foreach (var cue in events)
            {
                yield return cue;
            }

            heard = noted ? ListenAsync(listening, stop.Token) : heard;
        }

        await stop.CancelAsync();

        foreach (var cue in cues.Of(new Say(new ItemId($"{step.Calls[0].Item.Value}-reply"), ItemKind.Message, "Reply", [$"The harness answered: {string.Join(' ', round.Answers)}"])))
        {
            yield return cue;
        }
    }

    private async IAsyncEnumerable<IAgentEvent> ReturnedAsync(Round round, Task<ToolResult> answered, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        round.Waiting.Remove(answered);
        var result = await answered;
        yield return round.Cues.Returned(result.Item, result);
        yield return round.Cues.Closed(result.Item, Outcome(result));

        var early = Offers(ScenarioCatalog.WaitChild) ? ChildRequests.Early(result.Content, round.Answering) : Option<EarlyResult>.None;

        if (early.IsNone)
        {
            round.Answers.Add(result.Content);
            yield break;
        }

        var found = early.Match(asked => asked, () => new EarlyResult(Option<string>.None, string.Empty));

        await foreach (var cue in found.Answer.Match(answer => CallOnceAsync(round.Cues, ScenarioCatalog.AnswerChild, answer, cancellationToken), AsyncEnumerable.Empty<IAgentEvent>))
        {
            yield return cue;
        }

        var wait = new ItemId($"wait-child-{++called}");
        round.Waiting.Add(await PendingAsync(wait, cancellationToken));
        yield return round.Cues.Called(wait, ScenarioCatalog.WaitChild, found.Wait);
    }

    private async IAsyncEnumerable<IAgentEvent> NotedAsync(Cues cues, string message, Option<string> decision, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var cue in cues.Of(new Say(new ItemId($"noted-{++called}"), ItemKind.Message, "Reply", [$"Noted: {message} ", "I am folding it into this turn."])))
        {
            yield return cue;
        }

        var answer = decision.Bind(chosen => ChildRequests.Answer(message, chosen));

        await foreach (var cue in answer.Match(input => CallOnceAsync(cues, ScenarioCatalog.AnswerChild, input, cancellationToken), AsyncEnumerable.Empty<IAgentEvent>))
        {
            yield return cue;
        }
    }

    private async IAsyncEnumerable<IAgentEvent> CallOnceAsync(Cues cues, string tool, string input, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var item = new ItemId($"{tool.Replace('_', '-')}-{++called}");
        var pending = await PendingAsync(item, cancellationToken);
        yield return cues.Called(item, tool, input);

        var result = await pending;
        yield return cues.Returned(item, result);
        yield return cues.Closed(item, Outcome(result));
    }

    private async Task<Task<ToolResult>> PendingAsync(ItemId item, CancellationToken cancellationToken)
    {
        var pending = await gates.Tools.ExpectAsync(item, cancellationToken);

        return gates.Tools.AwaitAsync(item, pending, cancellationToken);
    }

    private Task<string> ListenAsync(Option<string> decision, CancellationToken cancellationToken) =>
        decision.Match(
            _ => gates.Messages.Reader.ReadAsync(cancellationToken).AsTask(),
            () => new TaskCompletionSource<string>().Task);

    private bool Offers(string tool) => options.Tools.Any(offered => offered.Name == tool && offered.Surface == ToolSurface.Executed);

    private static ItemOutcome Outcome(ToolResult result) => result.IsError ? ItemOutcome.Failed : ItemOutcome.Succeeded;

    private sealed record Round(Cues Cues, Option<string> Answering)
    {
        public List<Task<ToolResult>> Waiting { get; } = [];

        public List<string> Answers { get; } = [];
    }
}
