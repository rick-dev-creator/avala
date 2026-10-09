using Avala.Jobs.Contracts;
using Avala.Transcripts.Contracts;

namespace Avala.Workbench.Tests;

internal sealed class FakeTranscripts : ITranscripts
{
    public Dictionary<JobId, IReadOnlyList<KeptFact>> Kept { get; } = [];

    public ValueTask<IReadOnlyList<KeptFact>> EarlierRunsAsync(JobId job, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Kept.GetValueOrDefault(job, []));
}
