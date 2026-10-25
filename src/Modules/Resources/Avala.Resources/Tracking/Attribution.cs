using System.Collections.Immutable;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Resources.Contracts;
using Avala.Sdk;
using Avala.Sdk.Processes;

namespace Avala.Resources.Tracking;

internal sealed record SessionFacts(SessionId Session, string Home, string Provider, ConnectionName Connection)
{
    public Option<ProcessTreeId> Tree { get; init; }
}

internal sealed record Attribution(ImmutableDictionary<SessionId, SessionFacts> Sessions, ImmutableDictionary<SessionId, JobId> Jobs)
{
    public static Attribution Empty { get; } = new(ImmutableDictionary<SessionId, SessionFacts>.Empty, ImmutableDictionary<SessionId, JobId>.Empty);

    public ImmutableDictionary<string, JobId> Recovered { get; init; } = ImmutableDictionary<string, JobId>.Empty.WithComparers(StringComparer.Ordinal);

    public IEnumerable<string> Homes => Sessions.Values.Select(facts => facts.Home).Concat(Recovered.Keys).Distinct(StringComparer.Ordinal);

    public Attribution Known(string home, JobId job) => this with { Recovered = Recovered.SetItem(home, job) };

    public Attribution Opened(SessionFacts facts) => this with { Sessions = Sessions.SetItem(facts.Session, facts) };

    public Attribution Tied(SessionId session, JobId job) => this with { Jobs = Jobs.SetItem(session, job) };

    public Option<SessionFacts> Of(SessionId session) => Sessions.GetValueOrDefault(session).ToOption();

    public Option<JobId> JobOf(SessionId session) => Jobs.TryGetValue(session, out var job) ? job : Option<JobId>.None;

    public Option<SessionFacts> OfTree(ProcessTreeId tree) =>
        Sessions.Values.FirstOrDefault(facts => facts.Tree == Option<ProcessTreeId>.Some(tree)).ToOption();

    public IReadOnlyList<string> HomesOf(JobId job) =>
        [
            .. Jobs.Where(tied => tied.Value == job).SelectMany(tied => Of(tied.Key).Match<string[]>(facts => [facts.Home], () => []))
                .Concat(Recovered.Where(known => known.Value == job).Select(known => known.Key))
                .Distinct(StringComparer.Ordinal),
        ];

    public Option<JobId> JobAt(string home) =>
        Sessions.Values.Where(facts => facts.Home == home).Select(facts => JobOf(facts.Session)).FirstOrDefault(job => job.IsSome) is { IsSome: true } found
            ? found
            : Recovered.TryGetValue(home, out var known) ? known : Option<JobId>.None;

    public TreeUsage Attribute(TreeUsage usage) =>
        OfTree(usage.Tree).Match(
            facts => usage with
            {
                Session = facts.Session,
                Job = JobOf(facts.Session),
                Connection = facts.Connection,
                Provider = facts.Provider,
            },
            () => usage);
}
