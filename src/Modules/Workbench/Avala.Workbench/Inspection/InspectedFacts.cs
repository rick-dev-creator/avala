using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Workbench.Inspection;

internal enum InspectorDepth
{
    Essentials,
    Complete,
}

internal sealed class InspectedFacts(JobInspection inspection)
{
    private Reading latest = new(default, -1, Task.FromResult(Option<InspectorFacts>.None), Task.FromResult(Option<InspectorFacts>.None));

    public Task<Option<InspectorFacts>> ReadAsync(JobId job, int revision, InspectorDepth depth, CancellationToken cancellationToken)
    {
        var known = Volatile.Read(ref latest);

        if (known.Job != job || known.Revision != revision)
        {
            var essentials = inspection.EssentialsAsync(job, CancellationToken.None);
            known = new Reading(job, revision, essentials, inspection.CompleteAsync(essentials, CancellationToken.None));
            Volatile.Write(ref latest, known);
        }

        return (depth == InspectorDepth.Complete ? known.Complete : known.Essentials).WaitAsync(cancellationToken);
    }

    private sealed record Reading(JobId Job, int Revision, Task<Option<InspectorFacts>> Essentials, Task<Option<InspectorFacts>> Complete);
}
