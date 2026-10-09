using Avala.Jobs.Contracts;
using Avala.Transcripts.Contracts;

namespace Avala.Transcripts.Keeping;

internal interface ITranscriptLog
{
    void Keep(JobId job, DateTimeOffset at, ITranscriptFact fact);

    Task<int> AttemptsAsync(JobId job, CancellationToken cancellationToken);

    Task<IReadOnlyList<KeptFact>> EarlierRunsAsync(JobId job, CancellationToken cancellationToken);
}
