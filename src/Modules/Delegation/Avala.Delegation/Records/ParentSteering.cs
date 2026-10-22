using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Delegation.Records;

internal static class ParentSteering
{
    extension(IJobs jobs)
    {
        public ValueTask<Result<JobSteered, JobRejection>> NoteAsync(JobId parent, string note, CancellationToken cancellationToken) =>
            jobs.SteerAsync(parent, new JobMessage(note) { Author = MessageAuthor.Avala }, cancellationToken);
    }
}
