using Avala.Jobs.Contracts;
using Avala.Projects.Catalog;
using Avala.Projects.Contracts;
using Avala.Sdk;

namespace Avala.Projects.Placing;

internal sealed class ProjectsEntry(ProjectBook book, IJobCatalog jobs) : IProjects
{
    public ProjectMap Current => book.Current;

    public async ValueTask<Result<ConversationMoved, ProjectRejection>> MoveAsync(JobId job, ProjectId project, CancellationToken cancellationToken)
    {
        var history = await jobs.HistoryAsync(job, cancellationToken);

        return await book.WorkAsync(
            async token =>
            {
                var placed = history.Match(
                    found => book.Find(project).Match(target => target.Place(job, found.Summary.Parent), () => ProjectError.UnknownProject),
                    () => ProjectError.UnknownJob);

                return placed.TryGetValue(out var place, out var error)
                    ? Result<ConversationMoved, ProjectRejection>.Success(await book.PlaceAsync(place, token))
                    : Rejection(error);
            },
            cancellationToken);
    }

    private static ProjectRejection Rejection(ProjectError error) => error switch
    {
        ProjectError.UnknownProject => ProjectRejection.UnknownProject,
        ProjectError.UnknownJob => ProjectRejection.UnknownJob,
        _ => ProjectRejection.FollowsItsParent,
    };
}
