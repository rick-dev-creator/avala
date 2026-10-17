using Avala.Delegation.Contracts;
using Avala.Documentation.Contracts;
using Avala.Documentation.Features;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Workspaces.Contracts;

namespace Avala.Documentation.Writing;

internal sealed class Docfx(IProcessRunner processes, ISiteTemplate template)
{
    private bool? installed;

    public IReadOnlyList<SiteFile> Template => template.Files;

    public async Task<bool> InstalledAsync(CancellationToken cancellationToken) =>
        installed ??= (await processes.RunAsync(new ProcessRequest("docfx", ["--version"]), cancellationToken)).Match(outcome => outcome.Succeeded, _ => false);
}

internal sealed class SiteJobs(IJobs jobs, IRoleSettings roles, IJobCatalog catalog)
{
    public async Task<Option<JobId>> StartAsync(string repository, string root, CancellationToken cancellationToken)
    {
        var request = new JobRequest(repository, SiteBrief.Fill(root))
        {
            Model = await roles.OfRoleAsync(repository, ChildRole.Research, cancellationToken),
        };

        return (await jobs.SubmitAsync(request, cancellationToken)).Match(job => Option<JobId>.Some(job), _ => Option<JobId>.None);
    }

    public async Task<Option<JobId>> PreparingAsync(string repository, string root, CancellationToken cancellationToken) =>
        (await catalog.ListAsync(cancellationToken))
            .Where(job => job.Repository == repository && job.Instruction == SiteBrief.Fill(root) && job.Status is not (JobStatus.Approved or JobStatus.Discarded or JobStatus.Failed))
            .OrderByDescending(job => job.Submitted)
            .Select(job => Option<JobId>.Some(job.Job))
            .FirstOrDefault();
}

internal sealed class SiteDesk(DocumentationReads reads, IWorkingFiles files, Docfx docfx, SiteJobs jobs)
{
    public async Task<DocsSite> StateAsync(string folder, CancellationToken cancellationToken)
    {
        var docs = await reads.RepositoryAsync(folder, cancellationToken);
        var root = SiteLayout.RootOf(docs.Folder);
        var exists = (await files.ReadAsync(folder, $"{root}/{SiteLayout.Config}", cancellationToken)).Match(content => content.IsSome, _ => false);

        return new DocsSite(exists, root, await docfx.InstalledAsync(cancellationToken)) { Preparing = await jobs.PreparingAsync(folder, root, cancellationToken) };
    }

    public async Task<Result<SiteCreated, DocumentationError>> CreateAsync(string repository, CancellationToken cancellationToken)
    {
        var root = SiteLayout.RootOf((await reads.RepositoryAsync(repository, cancellationToken)).Folder);
        var started = await (await jobs.PreparingAsync(repository, root, cancellationToken)).Match(
            preparing => Task.FromResult(Option<JobId>.Some(preparing)),
            () => jobs.StartAsync(repository, root, cancellationToken));

        return started.Match(
            job => Result<SiteCreated, DocumentationError>.Success(new SiteCreated(root, job)),
            () => DocumentationError.NotSubmitted);
    }
}

internal sealed class SiteSkeleton(JobPlaces places, DocumentationReads reads, IWorkingFiles files, Docfx docfx) : IJobBriefing
{
    public async ValueTask<Option<string>> BriefAsync(JobId job, CancellationToken cancellationToken) =>
        await (await places.OfAsync(job, cancellationToken)).Match(
            place => LayOutAsync(place, cancellationToken),
            () => Task.FromResult(Option<string>.None));

    private async Task<Option<string>> LayOutAsync(JobPlace place, CancellationToken cancellationToken)
    {
        var docs = await reads.RepositoryAsync(place.Repository, cancellationToken);
        var root = SiteLayout.RootOf(docs.Folder);

        if (place.Instruction != SiteBrief.Fill(root))
        {
            return Option<string>.None;
        }

        var name = Path.GetFileName(place.Repository.TrimEnd('/', '\\'));
        var written = 0;

        foreach (var file in SiteLayout.Files(root, docs.Folder, name, [.. docs.Features.Select(listed => (listed.Feature.Slug, listed.Feature.Title))], docfx.Template))
        {
            if ((await files.ReadAsync(place.Worktree, file.Path, cancellationToken)).Match(found => found.IsNone, _ => false)
                && (await files.WriteAsync(place.Worktree, file.Path, file.Content, cancellationToken)).IsSuccess)
            {
                written++;
            }
        }

        return written == 0 ? Option<string>.None : $"Avala laid out the site in {root}/: {written} files to fill.";
    }
}
