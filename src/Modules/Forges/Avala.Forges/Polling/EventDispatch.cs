using Avala.Forges.Contracts;
using Avala.Forges.Sifting;
using Avala.Sdk;
using Avala.Triggers.Contracts;

namespace Avala.Forges.Polling;

internal sealed class EventDispatch(IEnumerable<ITriggers> triggers)
{
    private const int LongestDetail = 120;

    public async Task<IReadOnlyList<TriggerState>> ListAsync(CancellationToken cancellationToken)
    {
        var listed = new List<TriggerState>();

        foreach (var catalog in triggers.Take(1))
        {
            listed.AddRange((await catalog.ListAsync(cancellationToken)).Where(trigger => trigger.Forge.IsSome));
        }

        return listed;
    }

    public async Task<ForgeEventEntry> HandleAsync(TriggerState trigger, ForgeRule rule, ForgeEvent happened, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var judgement = EventFilter.Judge(rule, happened);
        var entry = new ForgeEventEntry(trigger.Id, at, happened.Kind, happened.Id, happened.Actor, judgement.Verdict)
        {
            Number = happened.Number,
            Title = happened.Title,
            Detail = Detail(happened),
            Reason = judgement.Reason,
        };

        if (!judgement.Passes)
        {
            return entry;
        }

        var request = new FireRequest(TriggerOrigin.External, $"forge {rule.Connection} · {happened.Actor}")
        {
            Payload = EventPayload.Json(rule, happened),
            Key = EventPayload.KeyOf(rule, happened),
        };

        var gone = entry with { Verdict = ForgeEventVerdict.Filtered, Reason = FilterReason.TriggerGone };

        return triggers.FirstOrDefault() is { } catalog
            ? (await catalog.FireAsync(trigger.Id, request, cancellationToken)).Match(
                run => entry with
                {
                    Verdict = run.Outcome == RunOutcome.Duplicate ? ForgeEventVerdict.Duplicate : ForgeEventVerdict.Fired,
                    Outcome = run.Outcome,
                    Job = run.Job,
                },
                _ => gone)
            : gone;
    }

    private static string Cut(string line) => line.Length > LongestDetail ? line[..LongestDetail] : line;

    private static Option<string> Detail(ForgeEvent happened) =>
        happened.Kind switch
        {
            ForgeEventKind.IssueLabeled => happened.Label,
            ForgeEventKind.CommentCommand => happened.Comment.Map(comment => Cut(comment.TrimStart().Split('\n', 2)[0].Trim())),
            ForgeEventKind.ChecksFailed => happened.Branch,
            ForgeEventKind.ReviewRequested => happened.Reviewer,
            _ => Option<string>.None,
        };
}
