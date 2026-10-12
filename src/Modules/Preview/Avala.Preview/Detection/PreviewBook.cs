using System.Collections.Immutable;
using Avala.Jobs.Contracts;
using Avala.Preview.Contracts;
using Avala.Preview.Origins;
using Avala.Sdk;

namespace Avala.Preview.Detection;

internal sealed record Sighting(string Worktree, Option<PortBlock> Lease, ImmutableHashSet<int> Listening, ImmutableList<PreviewTarget> Targets)
{
    public PreviewScope Scope => new(Lease, Listening, Worktree);
}

internal sealed class PreviewBook
{
    private ImmutableDictionary<JobId, Sighting> sighted = ImmutableDictionary<JobId, Sighting>.Empty;
    private ImmutableDictionary<JobId, PreviewTarget> served = ImmutableDictionary<JobId, PreviewTarget>.Empty;

    public ImmutableDictionary<JobId, Sighting> Sighted => Volatile.Read(ref sighted);

    public IReadOnlyList<PreviewTarget> Of(JobId job) =>
        [
            .. Volatile.Read(ref served).TryGetValue(job, out var page) ? [page] : Array.Empty<PreviewTarget>(),
            .. Sighted.TryGetValue(job, out var sighting) ? sighting.Targets : [],
        ];

    public PreviewScope ScopeOf(JobId job)
    {
        var scope = Sighted.TryGetValue(job, out var sighting) ? sighting.Scope : PreviewScope.Closed;

        return Volatile.Read(ref served).TryGetValue(job, out var page) ? scope with { Listening = scope.Listening.Add(page.Address.Port) } : scope;
    }

    public void Keep(ImmutableDictionary<JobId, Sighting> next) => Volatile.Write(ref sighted, next);

    public void Serve(PreviewTarget page) => ImmutableInterlocked.AddOrUpdate(ref served, page.Job, page, (_, _) => page);

    public Option<PreviewTarget> Unserve(JobId job) =>
        ImmutableInterlocked.TryRemove(ref served, job, out var page) ? page : Option<PreviewTarget>.None;
}
