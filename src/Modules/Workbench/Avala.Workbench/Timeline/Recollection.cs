using Avala.Jobs.Contracts;
using Avala.Transcripts.Contracts;

namespace Avala.Workbench.Timeline;

internal static class Recollection
{
    public static Transcript Recalled(string instruction, IReadOnlyList<AttemptRecord> attempts, IReadOnlyList<KeptFact> kept)
    {
        if (kept.Count == 0)
        {
            return Transcript.Empty.WithPrompts(instruction, attempts).WithRestart(kept: false);
        }

        var transcript = Transcript.Empty;
        var restarts = 0;

        for (var index = 0; index < kept.Count; index++)
        {
            if (index > 0 && kept[index].Run != kept[index - 1].Run)
            {
                transcript = transcript.Stopped(kept[index - 1].At).WithEarlierRestart(++restarts);
            }

            transcript = Replayed(transcript, instruction, attempts, kept[index]);
        }

        var marked = kept.Select(fact => fact.Fact).OfType<AttemptBegan>().Select(began => began.Attempt).DefaultIfEmpty().Max();

        return attempts.Where(attempt => attempt.Number <= marked)
            .Aggregate(transcript.Stopped(kept[^1].At), (recalled, attempt) => recalled.WithPrompt(instruction, attempt))
            .WithRestart(kept: true)
            .WithPrompts(instruction, attempts);
    }

    private static Transcript Replayed(Transcript transcript, string instruction, IReadOnlyList<AttemptRecord> attempts, KeptFact kept) => kept.Fact switch
    {
        AttemptBegan began => attempts.Where(attempt => attempt.Number == began.Attempt).Aggregate(transcript, (recalled, attempt) => recalled.WithPrompt(instruction, attempt)),
        AgentActed acted => transcript.Apply(acted.Event, kept.At),
        CanvasDrawn drawn => transcript.Apply(drawn.Snapshot),
        PermissionRuled ruled => transcript.Apply(ruled.Decision),
        FormRuled ruled => transcript.Apply(ruled.Decision),
        _ => transcript,
    };
}
