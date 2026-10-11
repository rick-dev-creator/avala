using System.Globalization;
using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Host.Tests;

internal sealed record DogfoodSeen(TimeSpan At, IIntegrationEvent Event);

internal sealed class DogfoodEvidence
{
    private readonly List<DogfoodSeen> seen = [];
    private readonly List<(string Key, string Value)> facts = [];
    private readonly List<string> findings = [];
    private readonly Dictionary<SessionId, SessionOpened> sessions = [];
    private readonly Dictionary<SessionId, JobId> sessionJobs = [];
    private readonly List<JobId> jobs = [];
    private readonly Dictionary<JobId, string> names = [];

    public IReadOnlyList<DogfoodSeen> Seen => seen;

    public IReadOnlyList<(string Key, string Value)> Facts => facts;

    public IReadOnlyList<string> Findings => findings;

    public IReadOnlyList<JobId> Jobs => jobs;

    public int Count => seen.Count;

    public string Outcome { get; set; } = "completed";

    public void Add(TimeSpan at, IIntegrationEvent happened)
    {
        seen.Add(new DogfoodSeen(at, happened));

        switch (happened)
        {
            case SessionOpened opened:
                sessions[opened.Session] = opened;
                break;
            case JobSessionStarted started:
                sessionJobs[started.Session] = started.Job;
                break;
            case JobSubmitted submitted when !jobs.Contains(submitted.Job):
                jobs.Add(submitted.Job);
                names[submitted.Job] = submitted.Parent.IsSome ? $"child-{jobs.Count}" : $"job-{jobs.Count}";
                break;
        }
    }

    public void Note(string key, string value) => facts.Add((key, value));

    public void Find(string finding) => findings.Add(finding);

    public IEnumerable<T> All<T>()
        where T : IIntegrationEvent => seen.Select(found => found.Event).OfType<T>();

    public IEnumerable<T> All<T>(Func<T, bool> match)
        where T : IIntegrationEvent => All<T>().Where(match);

    public Option<T> First<T>(Func<T, bool> match)
        where T : IIntegrationEvent => All<T>().Where(match).Take(1).Select(found => Option<T>.Some(found)).FirstOrDefault(Option<T>.None);

    public IEnumerable<IAgentEvent> Agent() => All<AgentActivity>().Select(activity => activity.Event);

    public IEnumerable<IAgentEvent> AgentOf(JobId job) => Agent().Where(agent => JobOf(agent.Session) == Option<JobId>.Some(job));

    public string Fact(string key) => facts.Where(fact => fact.Key == key).Select(fact => fact.Value).LastOrDefault() ?? string.Empty;

    public IEnumerable<string> FactsOf(string prefix) => facts.Where(fact => fact.Key.StartsWith(prefix, StringComparison.Ordinal)).Select(fact => fact.Value);

    public bool Has(string key) => facts.Any(fact => fact.Key == key);

    public Option<JobId> JobOf(SessionId session) => sessionJobs.TryGetValue(session, out var job) ? job : Option<JobId>.None;

    public IEnumerable<SessionOpened> SessionsOf(JobId job) =>
        sessionJobs.Where(pair => pair.Value == job).Select(pair => sessions.GetValueOrDefault(pair.Key)).OfType<SessionOpened>();

    public string ConnectionOf(SessionId session) => sessions.TryGetValue(session, out var opened) ? opened.Connection.Value : "unknown";

    public string AccountOf(SessionId session) =>
        sessions.TryGetValue(session, out var opened) ? opened.Account.Match(account => account.Id, () => "none") : "unknown";

    public string Name(JobId job) => names.GetValueOrDefault(job, job.ToString());

    public string Name(Option<JobId> job) => job.Match(Name, () => "no job");

    public Option<JobId> Job(int index) => jobs.Count > index ? jobs[index] : Option<JobId>.None;

    public IReadOnlyList<JobStatus> Journey(JobId job) => [.. All<JobProgressed>(progressed => progressed.Job == job).Select(progressed => progressed.Status)];

    public IReadOnlyList<UsageReported> Usage => [.. Agent().OfType<UsageReported>()];

    public decimal Spent => Usage.Sum(reported => reported.Cost.Match(cost => cost.Amount, () => 0m));

    public SortedDictionary<string, decimal> SpentBy(Func<SessionId, string> key)
    {
        var spent = new SortedDictionary<string, decimal>(StringComparer.Ordinal);

        foreach (var reported in Usage)
        {
            var name = key(reported.Session);
            spent[name] = spent.GetValueOrDefault(name) + reported.Cost.Match(cost => cost.Amount, () => 0m);
        }

        return spent;
    }

    public string When<T>(T happened)
        where T : IIntegrationEvent =>
        seen.FirstOrDefault(found => ReferenceEquals(found.Event, happened)) is { } found
            ? string.Create(CultureInfo.InvariantCulture, $"{typeof(T).Name} at {found.At:hh\\:mm\\:ss}")
            : typeof(T).Name;
}
