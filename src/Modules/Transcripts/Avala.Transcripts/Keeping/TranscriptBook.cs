using Avala.Jobs.Contracts;
using Avala.Transcripts.Contracts;

namespace Avala.Transcripts.Keeping;

internal sealed class TranscriptBook(ITranscriptLog log) : ITranscripts
{
    public async ValueTask<IReadOnlyList<KeptFact>> EarlierRunsAsync(JobId job, CancellationToken cancellationToken) =>
        await log.EarlierRunsAsync(job, cancellationToken);
}
