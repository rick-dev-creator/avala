using Avala.Forges.Contracts;
using Avala.Jobs.Contracts;
using Avala.Projects.Contracts;

namespace Avala.Resources.Housekeeping;

internal interface IWorktreeFacts
{
    bool Archived(JobId job);

    bool Merged(JobId job);

    bool AwaitsMerge(JobId job);
}

internal sealed class WorktreeFacts(IProjects projects, IPullRequests pullRequests) : IWorktreeFacts
{
    public bool Archived(JobId job) => projects.Current.Archived.Contains(job);

    public bool Merged(JobId job) => pullRequests.Of(job).Match(watch => watch.Ended == WatchEnd.Merged, () => false);

    public bool AwaitsMerge(JobId job) => pullRequests.Of(job).Match(watch => watch.Ended.IsNone, () => false);
}
