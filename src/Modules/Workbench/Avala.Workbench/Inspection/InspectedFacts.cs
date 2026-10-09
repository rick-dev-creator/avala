using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Workbench.Inspection;

internal sealed class InspectedFacts(JobInspection inspection)
{
    private Reading latest = new(default, -1, Task.FromResult(Option<InspectorFacts>.None));

    public Task<Option<InspectorFacts>> ReadAsync(JobId job, int revision, CancellationToken cancellationToken)
    {
        var known = Volatile.Read(ref latest);

        if (known.Job != job || known.Revision != revision)
        {
            known = new Reading(job, revision, inspection.ReadAsync(job, CancellationToken.None));
            Volatile.Write(ref latest, known);
        }

        return known.Facts.WaitAsync(cancellationToken);
    }

    private sealed record Reading(JobId Job, int Revision, Task<Option<InspectorFacts>> Facts);
}
