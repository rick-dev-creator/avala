using System.Text.RegularExpressions;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Forges.Watching;

internal sealed partial class ReportedLinks
{
    public const int KeptPerItem = 4_096;

    public const int KeptPerJob = 64;

    private readonly Dictionary<SessionId, JobId> sessions = [];
    private readonly Dictionary<(SessionId Session, ItemId Item), Said> said = [];
    private readonly Dictionary<JobId, List<Uri>> mentioned = [];
    private readonly Dictionary<JobId, List<Uri>> created = [];

    public int Open => said.Count;

    public void Started(JobId job, SessionId session) => sessions[session] = job;

    public IReadOnlyList<Uri> Of(JobId job)
    {
        var opened = created.TryGetValue(job, out var found) ? found : [];

        return [.. (mentioned.TryGetValue(job, out var named) ? named : []).Except(opened), .. opened];
    }

    public void Forget(JobId job)
    {
        _ = mentioned.Remove(job);
        _ = created.Remove(job);
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

    public void Saw(IAgentEvent activity)
    {
        switch (activity)
        {
            case ItemStarted { Kind: ItemKind.Message or ItemKind.Command } started when sessions.ContainsKey(started.Session):
                said[(started.Session, started.Item)] = new Said(string.Empty, Opens(started));
                break;
            case ItemProgressed progressed when said.TryGetValue((progressed.Session, progressed.Item), out var item) && sessions.TryGetValue(progressed.Session, out var job):
                var text = item.Carried + progressed.Text;
                said[(progressed.Session, progressed.Item)] = item with { Carried = Read(job, item.FromOpening, text, PullRequestLinks.Settled(text)) };
                break;
            case ItemCompleted completed when said.Remove((completed.Session, completed.Item), out var item) && sessions.TryGetValue(completed.Session, out var job):
                _ = Read(job, item.FromOpening, item.Carried, item.Carried.Length);
                break;
        }
    }

    private static bool Opens(ItemStarted started) =>
        started.Kind == ItemKind.Command && Opening().IsMatch($"{started.Title}\n{started.Input.Match(input => input, () => string.Empty)}");

    private string Read(JobId job, bool opens, string text, int settled)
    {
        var fresh = PullRequestLinks.In(text[..settled]).Where(PullRequestLinks.LooksLikePullRequest).Distinct().ToList();

        if (fresh.Count > 0)
        {
            var kept = opens ? created : mentioned;
            kept[job] = [.. (kept.TryGetValue(job, out var known) ? known : []).Except(fresh).Concat(fresh).TakeLast(KeptPerJob)];
        }

        var rest = text[settled..];

        return rest.Length > KeptPerItem ? string.Empty : rest;
    }

    [GeneratedRegex(@"\b(pr|pulls?|mr|merge-request)\s+create\b", RegexOptions.IgnoreCase)]
    private static partial Regex Opening();

    private readonly record struct Said(string Carried, bool FromOpening);
}
