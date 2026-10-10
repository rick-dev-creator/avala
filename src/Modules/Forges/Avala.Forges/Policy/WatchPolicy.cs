using Avala.Forges.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Forges.Policy;

internal sealed record Trigger(WakeReason Reason, string Key);

internal enum Verdict
{
    KeepWatching,
    WaitForJob,
    Wake,
    HoldForPerson,
    End,
}

internal sealed record WatchDecision(Verdict Verdict, Option<Trigger> Trigger, Option<WatchEnd> End)
{
    public static WatchDecision Ending(WatchEnd end) => new(Verdict.End, Option<Trigger>.None, end);

    public static WatchDecision Waiting { get; } = new(Verdict.WaitForJob, Option<Trigger>.None, Option<WatchEnd>.None);

    public static WatchDecision Watching(Option<Trigger> seen) => new(Verdict.KeepWatching, seen, Option<WatchEnd>.None);
}

internal static class WatchPolicy
{
    public static WatchDecision Decide(PullRequestWatchState watch, IReadOnlySet<string> handled, PullRequestState observed, JobStatus job)
    {
        if (observed.Lifecycle != PullRequestLifecycle.Open)
        {
            return WatchDecision.Ending(observed.Lifecycle == PullRequestLifecycle.Merged ? WatchEnd.Merged : WatchEnd.Closed);
        }

        if (job is JobStatus.Discarded or JobStatus.Failed)
        {
            return WatchDecision.Ending(WatchEnd.JobEnded);
        }

        if (job != JobStatus.Approved)
        {
            return WatchDecision.Waiting;
        }

        var trigger = TriggerOf(observed, watch.Policy);

        return trigger.Match(
            found => Acting(watch, handled, found),
            () => IsGreen(observed, watch.Policy) ? WatchDecision.Ending(WatchEnd.Green) : WatchDecision.Watching(Option<Trigger>.None));
    }

    public static Option<Trigger> TriggerOf(PullRequestState observed, OnPullRequest policy)
    {
        if (observed.Mergeability == Mergeability.Conflicting)
        {
            return new Trigger(WakeReason.Conflict, $"conflict:{observed.HeadCommit}");
        }

        if (observed.Checks.Any(check => check.Status == CheckStatus.Failed) && observed.Checks.All(check => check.Status != CheckStatus.Pending))
        {
            return new Trigger(WakeReason.ChecksFailed, $"checks:{observed.HeadCommit}");
        }

        return policy == OnPullRequest.WakeOnCiAndReviews
            ? Outstanding(observed.Reviews).FirstOrDefault(review => review.Verdict == ReviewVerdict.ChangesRequested).ToOption()
                .Map(review => new Trigger(WakeReason.ChangesRequested, $"review:{review.Id}"))
            : Option<Trigger>.None;
    }

    public static bool Covers(OnPullRequest policy, WakeReason reason) => policy switch
    {
        OnPullRequest.WakeOnCiAndReviews => true,
        OnPullRequest.WakeOnCi => reason is WakeReason.ChecksFailed or WakeReason.Conflict,
        _ => false,
    };

    private static WatchDecision Acting(PullRequestWatchState watch, IReadOnlySet<string> handled, Trigger trigger)
    {
        if (!Covers(watch.Policy, trigger.Reason) || handled.Contains(trigger.Key) || watch.Status == WatchStatus.NeedsPerson)
        {
            return WatchDecision.Watching(trigger);
        }

        return watch.WakeUps >= watch.MaxWakeUps
            ? new WatchDecision(Verdict.HoldForPerson, trigger, Option<WatchEnd>.None)
            : new WatchDecision(Verdict.Wake, trigger, Option<WatchEnd>.None);
    }

    private static bool IsGreen(PullRequestState observed, OnPullRequest policy) =>
        observed.Mergeability == Mergeability.Mergeable
        && observed.Checks.All(check => check.Status is CheckStatus.Passed or CheckStatus.Skipped)
        && (policy != OnPullRequest.WakeOnCiAndReviews || IsApproved(observed.Reviews));

    private static bool IsApproved(IReadOnlyList<Review> reviews)
    {
        var latest = Outstanding(reviews).ToList();

        return latest.Exists(review => review.Verdict == ReviewVerdict.Approved) && !latest.Exists(review => review.Verdict == ReviewVerdict.ChangesRequested);
    }

    private static IEnumerable<Review> Outstanding(IReadOnlyList<Review> reviews) =>
        reviews
            .Where(review => review.Verdict != ReviewVerdict.Commented)
            .GroupBy(review => review.Author, StringComparer.Ordinal)
            .Select(byAuthor => byAuthor.Last());
}

internal static class Backoff
{
    public const int MostDoublings = 4;

    public static TimeSpan Longest { get; } = TimeSpan.FromMinutes(30);

    public static TimeSpan After(TimeSpan poll, int failures)
    {
        if (failures <= 0)
        {
            return poll;
        }

        var backedOff = poll * (1 << Math.Min(failures, MostDoublings));

        return backedOff > Longest ? (poll > Longest ? poll : Longest) : backedOff;
    }
}
