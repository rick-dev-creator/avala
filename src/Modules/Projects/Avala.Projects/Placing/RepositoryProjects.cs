using Avala.Jobs.Contracts;
using Avala.Projects.Catalog;
using Avala.Projects.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Workspaces.Contracts;

namespace Avala.Projects.Placing;

internal sealed class RepositoryProjects(ProjectBook book, IJobCatalog jobs, IScratchRepositories scratch) : IStartupTask, IHandle<JobSubmitted>
{
    public StartupStage Stage => StartupStage.Restore;

    public Task RunAsync(CancellationToken cancellationToken) =>
        book.WorkAsync(
            async token =>
            {
                await book.LoadAsync(token);

                foreach (var job in await jobs.ListAsync(token))
                {
                    await EnsureAsync(job.Repository, token);
                }

                await book.AnnounceAsync(token);
            },
            cancellationToken);

    public async ValueTask HandleAsync(JobSubmitted integrationEvent, CancellationToken cancellationToken)
    {
        var history = await jobs.HistoryAsync(integrationEvent.Job, cancellationToken);
        var repository = history.Match(found => found.Summary.Repository, () => string.Empty);

        if (repository.Length > 0)
        {
            await book.WorkAsync(token => EnsureAsync(repository, token), cancellationToken);
        }
    }

    private async Task EnsureAsync(string repository, CancellationToken cancellationToken)
    {
        if (scratch.Holds(repository) || book.Holds(repository))
        {
            return;
        }

        if (Project.ForRepository(ProjectId.New(), repository, FolderOf(repository), book.Names).TryGetValue(out var project, out _))
        {
            await book.OpenAsync(project, cancellationToken);
        }
    }

    private static RepositoryFolder FolderOf(string repository)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(repository);
        var name = Path.GetFileName(trimmed);

        return new RepositoryFolder(name.Length > 0 ? name : trimmed, Path.GetFileName(Path.GetDirectoryName(trimmed) ?? string.Empty));
    }
}
