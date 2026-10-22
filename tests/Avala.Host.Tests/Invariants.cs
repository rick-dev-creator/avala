using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Workbench.Contracts.Presentation;
using Avala.Workspaces.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Host.Tests;

internal sealed record Scene(
    IReadOnlyList<JobHistory> Jobs,
    IReadOnlyList<SessionId> OpenSessions,
    IReadOnlyList<string> OpenTrees,
    IReadOnlyDictionary<JobId, string> Worktrees,
    IReadOnlyList<UsageSummary> Usage,
    IReadOnlyList<OpenTurn> TurnsOpenAfterTheirSession,
    IReadOnlyList<OpenItem> ItemsOpenAfterTheirTurn,
    IReadOnlyList<string> RestsWhileWorking,
    IReadOnlyList<SentMessage> Messages)
{
    public static async Task<Scene> ReadAsync(IServiceProvider services, TurnLedger turns, CancellationToken cancellationToken)
    {
        var catalog = services.GetRequiredService<IJobCatalog>();
        var agents = services.GetRequiredService<IAgents>();
        var usage = services.GetRequiredService<IUsage>();
        var workspaces = services.GetRequiredService<IWorkspaces>();
        var jobs = new List<JobHistory>();
        var worktrees = new Dictionary<JobId, string>();

        foreach (var summary in await catalog.ListAsync(cancellationToken))
        {
            jobs.AddRange((await catalog.HistoryAsync(summary.Job, cancellationToken)).Match<JobHistory[]>(history => [history], () => []));

            foreach (var workspace in summary.Workspace.Match<WorkspaceId[]>(found => [found], () => []))
            {
                _ = (await workspaces.FindAsync(workspace, cancellationToken)).TryGetValue(out var info, out _) && worktrees.TryAdd(summary.Job, info.Path);
            }
        }

        return new Scene(
            jobs,
            [.. jobs.SelectMany(job => job.Sessions).Select(session => session.Session).Distinct().Where(agents.IsOpen)],
            [.. services.GetRequiredService<IProcessTrees>().Open.Select(tree => tree.Home)],
            worktrees,
            [.. jobs.SelectMany(job => usage.OfJob(job.Summary.Job).Match<UsageSummary[]>(used => [used], () => [])), .. usage.ByProvider().Select(provider => provider.Usage)],
            turns.OpenAfterTheirSession,
            turns.OpenAfterTheirTurn,
            turns.RestedWhileWorking,
            services.GetRequiredService<MessageLedger>().Messages);
    }
}

internal static class Invariants
{
    private static readonly JobStatus[] Ended = [JobStatus.Approved, JobStatus.Discarded, JobStatus.Failed];

    private static readonly JobStatus[] Working = [JobStatus.Preparing, JobStatus.Running, JobStatus.Checking];

    public static IReadOnlyList<string> BrokenIn(Scene scene) =>
    [
        .. InstantlyBrokenIn(scene),
        .. StopsTheSessionsOfAnEndedJob(scene),
        .. LeavesNoProcessInTheWorktreeOfAnEndedJob(scene),
        .. EndsEveryChildOfAnEndedParent(scene),
        .. ClosesEveryTurnOfAnEndedSession(scene),
        .. ClosesEveryItemOfAnEndedTurn(scene),
        .. DeliversEveryMessageOnceOrReportsItUndelivered(scene),
    ];

    public static IReadOnlyList<string> InstantlyBrokenIn(Scene scene) =>
    [
        .. NeverFailsWithoutAReason(scene),
        .. HoldsOnlyWhileItNeedsHelp(scene),
        .. EndsExactlyWhenItIsApprovedDiscardedOrFailed(scene),
        .. SettlesEveryAttempt(scene),
        .. NeverCountsNegativeUsage(scene),
        .. scene.RestsWhileWorking,
    ];

    private static IEnumerable<string> NeverFailsWithoutAReason(Scene scene) =>
        from job in scene.Jobs
        where (job.Summary.Status == JobStatus.Failed) != job.Summary.Failure.IsSome
        select $"A job fails exactly when it records why: job {job.Summary.Job.Value} is {job.Summary.Status} with failure {Shown(job.Summary.Failure)}.";

    private static IEnumerable<string> HoldsOnlyWhileItNeedsHelp(Scene scene) =>
        from job in scene.Jobs
        where job.Summary.Hold.IsSome
            ? job.Summary.Status is not (JobStatus.NeedsHelp or JobStatus.Discarded)
            : job.Summary.Status == JobStatus.NeedsHelp && job.Attempts is not [.., { Outcome: AttemptOutcome.Rejected }]
        select $"A job's hold agrees with its status: job {job.Summary.Job.Value} is {job.Summary.Status} with hold {Shown(job.Summary.Hold)} after {(job.Attempts is [.., var last] ? last.Outcome.ToString() : "no attempt")}.";

    private static IEnumerable<string> EndsExactlyWhenItIsApprovedDiscardedOrFailed(Scene scene) =>
        from job in scene.Jobs
        where job.Summary.Ended.IsSome != Ended.Contains(job.Summary.Status)
        select $"A job has ended exactly when it is approved, discarded or failed: job {job.Summary.Job.Value} is {job.Summary.Status} with end {Shown(job.Summary.Ended)}.";

    private static IEnumerable<string> SettlesEveryAttempt(Scene scene) =>
        from job in scene.Jobs
        let underway = job.Attempts.Where(attempt => attempt.Outcome is AttemptOutcome.Running or AttemptOutcome.AwaitingCheck).ToList()
        where underway.Count > (Working.Contains(job.Summary.Status) ? 1 : 0) || (underway is [var only] && only != job.Attempts[^1])
        select $"Only the last attempt of a working job is underway: job {job.Summary.Job.Value} is {job.Summary.Status} with attempts {string.Join(", ", underway.Select(attempt => attempt.Number))} underway of {job.Attempts.Count}.";

    private static IEnumerable<string> StopsTheSessionsOfAnEndedJob(Scene scene) =>
        from job in scene.Jobs
        where Ended.Contains(job.Summary.Status)
        from session in job.Sessions
        where scene.OpenSessions.Contains(session.Session)
        select $"An ended job stops its session: job {job.Summary.Job.Value} is {job.Summary.Status} and its session {session.Session.Value} is still open.";

    private static IEnumerable<string> LeavesNoProcessInTheWorktreeOfAnEndedJob(Scene scene) =>
        from job in scene.Jobs
        where Ended.Contains(job.Summary.Status) && scene.Worktrees.ContainsKey(job.Summary.Job)
        let worktree = scene.Worktrees[job.Summary.Job]
        from home in scene.OpenTrees
        where home == worktree || home.StartsWith(worktree + Path.DirectorySeparatorChar, StringComparison.Ordinal)
        select $"An ended job leaves no process behind: job {job.Summary.Job.Value} is {job.Summary.Status} and a process tree is still open in {home}.";

    private static IEnumerable<string> EndsEveryChildOfAnEndedParent(Scene scene) =>
        from child in scene.Jobs
        where !Ended.Contains(child.Summary.Status)
        from parent in scene.Jobs
        where child.Summary.Parent == Option<JobId>.Some(parent.Summary.Job) && Ended.Contains(parent.Summary.Status)
        select $"No child outlives its parent's end: job {child.Summary.Job.Value} is {child.Summary.Status} while its parent {parent.Summary.Job.Value} is {parent.Summary.Status}.";

    private static IEnumerable<string> ClosesEveryTurnOfAnEndedSession(Scene scene) =>
        scene.TurnsOpenAfterTheirSession.Select(turn => $"No turn stays open once its session ended: turn {turn.Turn.Value} of session {turn.Session.Value}.");

    private static IEnumerable<string> ClosesEveryItemOfAnEndedTurn(Scene scene) =>
        scene.ItemsOpenAfterTheirTurn.Select(item => $"No item stays open once its turn ended: item {item.Item.Value} of turn {item.Turn.Value} in session {item.Session.Value}.");

    private static IEnumerable<string> DeliversEveryMessageOnceOrReportsItUndelivered(Scene scene) =>
        from message in scene.Messages
        let job = scene.Jobs.FirstOrDefault(found => found.Summary.Job == message.Job)
        let failedOnIt = job is { Summary.Status: JobStatus.Failed, Attempts: [.., var last] } && last.Number - message.Attempt is 0 or 1
        let ended = job is { Summary.Status: JobStatus.Discarded or JobStatus.Failed }
        where message.Reached != (message.Accepted ? 1 : 0) && !(message.Accepted && failedOnIt && message.Reached == 0)
            || (message.Queue == Option<QueueChange>.Some(QueueChange.Taken) && !message.Accepted)
            || (message.Queue == Option<QueueChange>.Some(QueueChange.Queued) && ended)
        select $"Every message reaches the agent once, or is reported undelivered: message {message.Message.Value} to job {message.Job.Value} was {(message.Accepted ? "accepted" : "refused")}{message.Queue.Match(change => $", last {change} in the Workbench's queue", () => string.Empty)} and reached the agent {message.Reached} times.";

    private static IEnumerable<string> NeverCountsNegativeUsage(Scene scene) =>
        from usage in scene.Usage
        where Negative(usage.Tokens) || usage.Costs.Any(cost => cost.Amount < 0) || usage.UnpricedReports < 0
            || usage.Turns.Finished < 0 || usage.Turns.Interrupted < 0 || usage.Turns.Failed < 0 || usage.Turns.Duration < TimeSpan.Zero
            || usage.Limits.Any(limit => limit.UsedFraction < 0)
        select $"Usage is never negative: {usage}.";

    private static bool Negative(TokenUsage tokens) =>
        tokens.Input < 0 || tokens.Output < 0 || tokens.CacheRead < 0 || tokens.CacheWrite < 0 || tokens.Reasoning < 0;

    private static string Shown<T>(Option<T> value)
        where T : notnull =>
        value.Match(found => found.ToString() ?? string.Empty, () => "none");
}
