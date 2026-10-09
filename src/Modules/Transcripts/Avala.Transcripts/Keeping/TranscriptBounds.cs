using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Canvas.Contracts;
using Avala.Sdk;

namespace Avala.Transcripts.Keeping;

internal sealed class TranscriptBounds
{
    public const int ItemText = 32_000;

    public const int CanvasText = 1_000_000;

    public const string Cut = "\n[...] the rest was not kept";

    private readonly Dictionary<(TurnId Turn, ItemId Item), int> streamed = [];

    public Option<IAgentEvent> Bounded(IAgentEvent activity) => activity switch
    {
        LimitReported or ResumeTokenIssued => Option<IAgentEvent>.None,
        ItemStarted started => started with { Input = started.Input.Map(Bounded) },
        ItemProgressed progressed => Streamed(progressed),
        ItemCompleted completed => Forgotten(completed, key => key == (completed.Turn, completed.Item)),
        TurnCompleted completed => Forgotten(completed, key => key.Turn == completed.Turn),
        ToolCalled called => called with { Input = Bounded(called.Input) },
        ToolReturned returned => returned with { Result = returned.Result with { Content = Bounded(returned.Result.Content) } },
        MessageQueued queued => queued with { Text = Bounded(queued.Text) },
        _ => Option<IAgentEvent>.Some(activity),
    };

    public static CanvasSnapshot Bounded(CanvasSnapshot snapshot) =>
        snapshot.Content.Length <= CanvasText ? snapshot : snapshot with { Content = string.Empty };

    private static string Bounded(string text) => text.Length <= ItemText ? text : text[..ItemText] + Cut;

    private Option<IAgentEvent> Streamed(ItemProgressed progressed)
    {
        var key = (progressed.Turn, progressed.Item);
        var kept = streamed.GetValueOrDefault(key);

        if (kept > ItemText)
        {
            return Option<IAgentEvent>.None;
        }

        if (progressed.Text.Length <= ItemText - kept)
        {
            streamed[key] = kept + progressed.Text.Length;

            return progressed;
        }

        streamed[key] = ItemText + 1;

        return progressed with { Text = progressed.Text[..(ItemText - kept)] + Cut };
    }

    private Option<IAgentEvent> Forgotten(IAgentEvent ended, Func<(TurnId Turn, ItemId Item), bool> ending)
    {
        foreach (var key in streamed.Keys.Where(ending).ToList())
        {
            streamed.Remove(key);
        }

        return Option<IAgentEvent>.Some(ended);
    }
}
