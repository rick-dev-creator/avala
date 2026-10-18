using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;

namespace Avala.Forges.Watching;

internal sealed class ReportedLinks
{
    public const int KeptPerItem = 4_096;

    public const int KeptPerJob = 64;

    private readonly Dictionary<SessionId, JobId> sessions = [];
    private readonly Dictionary<(SessionId Session, ItemId Item), string> said = [];
    private readonly Dictionary<JobId, List<Uri>> links = [];

    public int Open => said.Count;

    public void Started(JobId job, SessionId session) => sessions[session] = job;

    public IReadOnlyList<Uri> Of(JobId job) => links.TryGetValue(job, out var found) ? [.. found] : [];

    public void Forget(JobId job)
    {
        _ = links.Remove(job);
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
                said[(started.Session, started.Item)] = string.Empty;
                break;
            case ItemProgressed progressed when said.TryGetValue((progressed.Session, progressed.Item), out var carried) && sessions.TryGetValue(progressed.Session, out var job):
                said[(progressed.Session, progressed.Item)] = Read(job, carried + progressed.Text, PullRequestLinks.Settled(carried + progressed.Text));
                break;
            case ItemCompleted completed when said.Remove((completed.Session, completed.Item), out var carried) && sessions.TryGetValue(completed.Session, out var job):
                _ = Read(job, carried, carried.Length);
                break;
        }
    }

    private string Read(JobId job, string text, int settled)
    {
        var known = links.TryGetValue(job, out var found) ? found : [];
        var fresh = PullRequestLinks.In(text[..settled]).Where(PullRequestLinks.LooksLikePullRequest).Distinct().ToList();

        if (fresh.Count > 0)
        {
            links[job] = [.. known.Except(fresh).Concat(fresh).TakeLast(KeptPerJob)];
        }

        var rest = text[settled..];

        return rest.Length > KeptPerItem ? string.Empty : rest;
    }
}
