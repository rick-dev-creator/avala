using Avala.Jobs.Contracts;
using Avala.Projects.Catalog;
using Avala.Projects.Contracts;
using Avala.Sdk;

namespace Avala.Projects.Placing;

internal sealed class ProjectsEntry(ProjectBook book, IJobCatalog jobs) : IProjects
{
    public ProjectMap Current => book.Current;

    public ValueTask<Result<ProjectCreated, ProjectRejection>> CreateAsync(string name, CancellationToken cancellationToken) =>
        DecideAsync(
            token => Project.Create(ProjectId.New(), name, book.Names).TryGetValue(out var project, out var error)
                ? SucceededAsync(book.OpenAsync(project, token))
                : FailedAsync<ProjectCreated>(error),
            cancellationToken);

    public ValueTask<Result<ProjectRenamed, ProjectRejection>> RenameAsync(ProjectId project, string name, CancellationToken cancellationToken) =>
        DecideAsync(
            token => book.Find(project).Match(found => found.Rename(name, book.Names), () => ProjectError.UnknownProject).TryGetValue(out var named, out var error)
                ? SucceededAsync(book.RenameAsync(named, token))
                : FailedAsync<ProjectRenamed>(error),
            cancellationToken);

    public async ValueTask<Result<ProjectDeleted, ProjectRejection>> DeleteAsync(ProjectId project, CancellationToken cancellationToken)
    {
        var all = await jobs.ListAsync(cancellationToken);

        return await DecideAsync(
            token =>
            {
                var held = all.Count(job => job.Parent.IsNone && book.Current.Of(job.Job, job.Repository).Match(found => found.Project == project, () => false));

                return book.Find(project).Match(found => found.Delete(held), () => ProjectError.UnknownProject).TryGetValue(out var removed, out var error)
                    ? SucceededAsync(book.RemoveAsync(removed, token))
                    : FailedAsync<ProjectDeleted>(error);
            },
            cancellationToken);
    }

    public async ValueTask<Result<ConversationMoved, ProjectRejection>> MoveAsync(JobId job, ProjectId project, CancellationToken cancellationToken)
    {
        var history = await jobs.HistoryAsync(job, cancellationToken);

        return await DecideAsync(
            token => history.Match(
                    found => book.Find(project).Match(target => target.Place(job, found.Summary.Parent), () => ProjectError.UnknownProject),
                    () => ProjectError.UnknownJob)
                .TryGetValue(out var placed, out var error)
                ? SucceededAsync(book.PlaceAsync(placed, token))
                : FailedAsync<ConversationMoved>(error),
            cancellationToken);
    }

    private async ValueTask<Result<TEvent, ProjectRejection>> DecideAsync<TEvent>(Func<CancellationToken, Task<Result<TEvent, ProjectRejection>>> decide, CancellationToken cancellationToken) =>
        await book.WorkAsync(decide, cancellationToken);

    private static async Task<Result<TEvent, ProjectRejection>> SucceededAsync<TEvent>(Task<TEvent> done) =>
        Result<TEvent, ProjectRejection>.Success(await done);

    private static Task<Result<TEvent, ProjectRejection>> FailedAsync<TEvent>(ProjectError error) =>
        Task.FromResult(Result<TEvent, ProjectRejection>.Failure(Enum.Parse<ProjectRejection>(error.ToString())));
}
