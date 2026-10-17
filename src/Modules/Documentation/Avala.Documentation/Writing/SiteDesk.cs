using Avala.Delegation.Contracts;
using Avala.Documentation.Contracts;
using Avala.Documentation.Features;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Workspaces.Contracts;

namespace Avala.Documentation.Writing;

internal sealed class Docfx(IProcessRunner processes)
{
    private bool? installed;

    public async Task<bool> InstalledAsync(CancellationToken cancellationToken) =>
        installed ??= (await processes.RunAsync(new ProcessRequest("docfx", ["--version"]), cancellationToken)).Match(outcome => outcome.Succeeded, _ => false);
}

internal sealed class SiteFiller(IJobs jobs, IRoleSettings roles)
{
    public async Task<Option<JobId>> StartAsync(string repository, string root, CancellationToken cancellationToken)
    {
        var request = new JobRequest(repository, SiteBrief.Fill(root))
        {
            Model = await roles.OfRoleAsync(repository, ChildRole.Research, cancellationToken),
        };

        return (await jobs.SubmitAsync(request, cancellationToken)).Match(job => Option<JobId>.Some(job), _ => Option<JobId>.None);
    }
}

internal sealed class SiteDesk(DocumentationReads reads, IWorkingFiles files, Docfx docfx, SiteFiller filler)
{
    public async Task<DocsSite> StateAsync(string folder, CancellationToken cancellationToken)
    {
        var docs = await reads.RepositoryAsync(folder, cancellationToken);
        var root = SiteLayout.RootOf(docs.Folder);
        var exists = (await files.ReadAsync(folder, $"{root}/{SiteLayout.Config}", cancellationToken)).Match(content => content.IsSome, _ => false);

        return new DocsSite(exists, root, await docfx.InstalledAsync(cancellationToken));
    }

    public async Task<Result<SiteCreated, DocumentationError>> CreateAsync(string repository, CancellationToken cancellationToken)
    {
        var docs = await reads.RepositoryAsync(repository, cancellationToken);
        var root = SiteLayout.RootOf(docs.Folder);
        var name = Path.GetFileName(repository.TrimEnd('/', '\\'));
        var written = new List<string>();

        foreach (var file in SiteLayout.Files(root, docs.Folder, name, [.. docs.Features.Select(listed => (listed.Feature.Slug, listed.Feature.Title))]))
        {
            var current = await files.ReadAsync(repository, file.Path, cancellationToken);

            if (!current.TryGetValue(out var found, out _))
            {
                return DocumentationError.Unreadable;
            }

            if (found.IsSome)
            {
                continue;
            }

            if (!(await files.WriteAsync(repository, file.Path, file.Content, cancellationToken)).TryGetValue(out _, out _))
            {
                return DocumentationError.Unwritable;
            }

            written.Add(file.Path);
        }

        return new SiteCreated(root, written) { Filler = await filler.StartAsync(repository, root, cancellationToken) };
    }
}
