using Avala.Jobs.Contracts;
using Avala.Projects.Catalog;
using Avala.Sdk.Events;

namespace Avala.Projects.Placing;

internal sealed class ArchiveReturns(ProjectBook book) : IHandle<JobProgressed>
{
    public async ValueTask HandleAsync(JobProgressed integrationEvent, CancellationToken cancellationToken)
    {
        if (Archive.Returns(integrationEvent.Status))
        {
            await book.WorkAsync(token => ReturnAsync(book, integrationEvent.Job, token), cancellationToken);
        }
    }

    public static Task ReturnAsync(ProjectBook book, JobId job, CancellationToken cancellationToken) =>
        Archive.Restore(job, book.Current.Archived).TryGetValue(out var restored, out _)
            ? book.RestoreAsync(restored, cancellationToken)
            : Task.CompletedTask;
}
