using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;

namespace Avala.Forges.Watching;

internal sealed class ReportedLinks
{
    public const int KeptPerItem = 65_536;

    private readonly Dictionary<SessionId, JobId> sessions = [];
    private readonly Dictionary<(SessionId Session, ItemId Item), string> said = [];
    private readonly Dictionary<JobId, List<Uri>> links = [];

    public void Started(JobId job, SessionId session) => sessions[session] = job;

    public IReadOnlyList<Uri> Of(JobId job) => links.TryGetValue(job, out var found) ? [.. found] : [];

    public void Forget(JobId job)
    {
        _ = links.Remove(job);

        foreach (var session in sessions.Where(pair => pair.Value == job).Select(pair => pair.Key).ToList())
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
            case ItemProgressed progressed when said.TryGetValue((progressed.Session, progressed.Item), out var text):
                said[(progressed.Session, progressed.Item)] = Kept(text + progressed.Text);
                break;
            case ItemCompleted completed when said.Remove((completed.Session, completed.Item), out var text) && sessions.TryGetValue(completed.Session, out var job):
                Add(job, PullRequestLinks.In(text));
                break;
        }
    }

    private void Add(JobId job, IEnumerable<Uri> found) =>
        links[job] = [.. links.TryGetValue(job, out var known) ? known : [], .. found];

    private static string Kept(string text) => text.Length > KeptPerItem ? text[^KeptPerItem..] : text;
}
