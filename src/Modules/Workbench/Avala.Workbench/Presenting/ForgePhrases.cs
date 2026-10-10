using System.Globalization;
using Avala.Forges.Contracts;
using Avala.Jobs.Contracts;

namespace Avala.Workbench.Presenting;

internal static class ForgePhrases
{
    public static string Error(ForgeError error) => error switch
    {
        ForgeError.UnknownConnection => "the repository names a forge that forges.json does not declare.",
        ForgeError.UnknownForge => "no plugin of that forge is installed.",
        ForgeError.MissingUrl => "the forge connection needs a url.",
        ForgeError.MissingCredential => "the token's environment variable is not set.",
        ForgeError.CliUnavailable => "the forge's command-line tool is not installed.",
        ForgeError.Unauthorized => "the forge refused the credential.",
        ForgeError.NotFound => "the forge does not know the repository or the pull request.",
        ForgeError.RateLimited => "the forge's rate limit is spent for now.",
        ForgeError.Rejected => "the forge rejected the request.",
        ForgeError.Unreachable => "the forge could not be reached.",
        ForgeError.InvalidRemote => "the remote's url names no owner and repository.",
        ForgeError.NoBaseBranch => "the job's base is not a branch.",
        ForgeError.PushRejected => "the remote refused the push of the job's branch.",
        ForgeError.GitFailed => "git could not read the remote.",
        ForgeError.MissingForge => "the repository's .avala/jobs.json has no pullRequest section naming a forge.",
        _ => $"forges.json or the pullRequest section is invalid ({error}).",
    };

    public static string Status(PullRequestWatchState state) => state.Status switch
    {
        WatchStatus.Watching => $"Watching{Next(state)}",
        WatchStatus.BackingOff => string.Create(
            CultureInfo.InvariantCulture,
            $"Retrying after {state.Failure.Match(error => error.ToString(), () => "an error")} · {state.Failures} failure{(state.Failures == 1 ? string.Empty : "s")}{Next(state)}"),
        WatchStatus.WaitingForJob => state.WakeUps > 0
            ? string.Create(CultureInfo.InvariantCulture, $"The agent works on wake-up {state.WakeUps} of {state.MaxWakeUps}")
            : "Waiting for the job",
        WatchStatus.NeedsPerson => $"Held for a person: {state.Pending.Match(Reason, () => "the wake-ups are spent")}",
        _ => $"Watch ended: {state.Ended.Match(End, () => "ended")}",
    };

    public static string Policy(PullRequestWatchState state) =>
        state.Policy != OnPullRequest.WatchOnly && state.Redelivery == Redelivery.Review
            ? $"{Policy(state.Policy, state.MaxWakeUps)}; each fix comes back to you for review"
            : Policy(state.Policy, state.MaxWakeUps);

    public static string Policy(OnPullRequest policy, int wakeUps) => policy switch
    {
        OnPullRequest.WakeOnCi => string.Create(CultureInfo.InvariantCulture, $"Wakes the agent on CI failures and conflicts, at most {wakeUps} times"),
        OnPullRequest.WakeOnCiAndReviews => string.Create(CultureInfo.InvariantCulture, $"Wakes the agent on CI failures, conflicts and reviews, at most {wakeUps} times"),
        _ => "Watches only, never wakes the agent",
    };

    public static string Reason(WakeReason reason) => reason switch
    {
        WakeReason.Conflict => "conflicts with its base",
        WakeReason.ChecksFailed => "checks failed",
        _ => "changes requested",
    };

    public static string End(WatchEnd end) => end switch
    {
        WatchEnd.Merged => "merged",
        WatchEnd.Closed => "closed",
        WatchEnd.Green => "checks passed",
        _ => "the job ended",
    };

    public static string Checks(PullRequestState state) =>
        state.Checks.Count == 0
            ? "No checks"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{state.Checks.Count(check => check.Status == CheckStatus.Passed)} passed · {state.Checks.Count(check => check.Status == CheckStatus.Failed)} failed · {state.Checks.Count(check => check.Status == CheckStatus.Pending)} pending");

    public static string Check(CheckRun check) =>
        $"{check.Status switch { CheckStatus.Passed => "passed", CheckStatus.Failed => "failed", CheckStatus.Pending => "pending", _ => "skipped" }} · {check.Name}{(check.Summary.Length > 0 ? $": {check.Summary}" : string.Empty)}";

    public static string Review(Forges.Contracts.Review review) =>
        $"{review.Author} {review.Verdict switch { ReviewVerdict.Approved => "approved", ReviewVerdict.ChangesRequested => "requested changes", _ => "commented" }}";

    public static string Mergeability(Mergeability mergeability) => mergeability switch
    {
        Forges.Contracts.Mergeability.Conflicting => "Conflicts with its base",
        Forges.Contracts.Mergeability.Mergeable => "No conflicts",
        _ => "Mergeability not known yet",
    };

    public static string WakeUp(WakeUpRecord wakeUp) => wakeUp.Outcome switch
    {
        WakeOutcome.Woken => $"Woke the agent: {Reason(wakeUp.Reason)}, {wakeUp.Conversation.Match(Conversation, () => "in a new session")}",
        WakeOutcome.Refused => $"Could not wake the agent ({Reason(wakeUp.Reason)}): {wakeUp.Refusal.Match(rejection => rejection.ToString(), () => "refused")}",
        _ => $"Held for a person: {Reason(wakeUp.Reason)}, comment posted",
    };

    private static string Conversation(ContinuedIn conversation) => conversation switch
    {
        ContinuedIn.ResumedConversation => "resuming its conversation",
        ContinuedIn.SameSession => "in its session",
        _ => "in a new conversation",
    };

    private static string Next(PullRequestWatchState state) =>
        state.NextPoll.Match(next => string.Create(CultureInfo.InvariantCulture, $" · next check {next.ToLocalTime():HH:mm}"), () => string.Empty);
}
