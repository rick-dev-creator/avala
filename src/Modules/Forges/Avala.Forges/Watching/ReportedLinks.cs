using System.Text.RegularExpressions;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Forges.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Forges.Watching;

internal sealed record PushedBranch(RepositoryAddress Repository, string Branch);

internal sealed record Reported(IReadOnlyList<Uri> Mentioned, IReadOnlyList<Uri> Created, IReadOnlyList<PushedBranch> Pushed)
{
    public static Reported Nothing { get; } = new([], [], []);

    public IReadOnlyList<Uri> Links => [.. Mentioned.Except(Created), .. Created];
}

internal sealed partial class ReportedLinks
{
    public const int KeptPerItem = 4_096;

    public const int KeptPerJob = 64;

    private readonly Dictionary<SessionId, JobId> sessions = [];
    private readonly Dictionary<(SessionId Session, ItemId Item), Said> said = [];
    private readonly Dictionary<JobId, Reported> reported = [];

    public int Open => said.Count;

    public void Started(JobId job, SessionId session) => sessions[session] = job;

    public Reported Of(JobId job) => reported.TryGetValue(job, out var found) ? found : Reported.Nothing;

    public void Restore(JobId job, Reported kept) => reported[job] = kept;

    public void Forget(JobId job)
    {
        _ = reported.Remove(job);
        var ended = sessions.Where(pair => pair.Value == job).Select(pair => pair.Key).ToHashSet();

        foreach (var key in said.Keys.Where(key => ended.Contains(key.Session)).ToList())
        {
            _ = said.Remove(key);
        }

        foreach (var session in ended)
        {
            _ = sessions.Remove(session);
        }
    }

    public Option<JobId> Saw(IAgentEvent activity)
    {
        switch (activity)
        {
            case ItemStarted { Kind: ItemKind.Message or ItemKind.Command or ItemKind.Mcp } started when sessions.ContainsKey(started.Session):
                said[(started.Session, started.Item)] = new Said(string.Empty, Opens(started), Pushes(started), []);

                return Option<JobId>.None;
            case ItemProgressed progressed when said.TryGetValue((progressed.Session, progressed.Item), out var item) && sessions.TryGetValue(progressed.Session, out var job):
                return Progressed(job, progressed, item);
            case ItemCompleted completed when said.Remove((completed.Session, completed.Item), out var item) && sessions.TryGetValue(completed.Session, out var job):
                return Completed(job, completed, item);
            default:
                return Option<JobId>.None;
        }
    }

    private Option<JobId> Progressed(JobId job, ItemProgressed progressed, Said item)
    {
        var text = item.Carried + progressed.Text;
        var (carried, found, opened) = Read(job, item, text, PullRequestLinks.Settled(text));
        said[(progressed.Session, progressed.Item)] = item with { Carried = carried, Opened = opened };

        return found ? job : Option<JobId>.None;
    }

    private Option<JobId> Completed(JobId job, ItemCompleted completed, Said item)
    {
        var read = Read(job, item, item.Carried, item.Carried.Length).Found;
        var pushed = completed.Outcome == ItemOutcome.Succeeded && Pushed(job, item.Branches);

        return read || pushed ? job : Option<JobId>.None;
    }

    private static bool Opens(ItemStarted started) =>
        started.Kind is ItemKind.Command or ItemKind.Mcp && Opening().IsMatch($"{started.Title}\n{started.Input.Match(input => input, () => string.Empty)}");

    private static IReadOnlyList<PushedBranch> Pushes(ItemStarted started) =>
        started.Kind == ItemKind.Command ? GitPushes.In(started.Input.Match(input => input, () => started.Title)) : [];

    private static Uri[] Kept(IReadOnlyList<Uri> known, List<Uri> fresh) => [.. known.Except(fresh).Concat(fresh).TakeLast(KeptPerJob)];

    private bool Pushed(JobId job, IReadOnlyList<PushedBranch> pushes)
    {
        if (pushes.Count == 0)
        {
            return false;
        }

        var known = Of(job);
        reported[job] = known with { Pushed = [.. known.Pushed.Except(pushes).Concat(pushes).TakeLast(KeptPerJob)] };

        return true;
    }

    private (string Carried, bool Found, IReadOnlyList<Uri> Opened) Read(JobId job, Said item, string text, int settled)
    {
        var fresh = PullRequestLinks.In(text[..settled]).Where(PullRequestLinks.LooksLikePullRequest).Distinct().ToList();
        var opened = item.FromOpening ? [.. item.Opened.Concat(fresh).Distinct()] : item.Opened;

        if (fresh.Count > 0)
        {
            var known = Of(job);
            reported[job] = item.FromOpening
                ? known with { Created = [.. known.Created.Except(opened).Concat(opened.Reverse()).TakeLast(KeptPerJob)] }
                : known with { Mentioned = Kept(known.Mentioned, fresh) };
        }

        var rest = text[settled..];

        return (rest.Length > KeptPerItem ? string.Empty : rest, fresh.Count > 0, opened);
    }

    [GeneratedRegex(@"\b(pr|pulls?|mr|merge-request)\s+create\b|create[_-]?(pull|merge)[_-]?request", RegexOptions.IgnoreCase)]
    private static partial Regex Opening();

    private readonly record struct Said(string Carried, bool FromOpening, IReadOnlyList<PushedBranch> Branches, IReadOnlyList<Uri> Opened);
}
