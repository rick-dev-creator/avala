using Avala.Documentation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Preview.Contracts;
using Avala.Sdk;
using Avala.Workbench.Board;

namespace Avala.Workbench.Documenting;

internal sealed class DocsReader(JobBoard board, IDocumentation documentation, IPreviews previews)
{
    public bool Offers(JobId job) => board.Docs.ContainsKey(job);

    public Task<Option<JobDocs>> JobAsync(JobId job, CancellationToken cancellationToken) => documentation.JobDocsAsync(job, cancellationToken);

    public async Task<Option<PreviewError>> OpenSiteAsync(JobId job, DocsSite site, CancellationToken cancellationToken) =>
        (await previews.RunSiteAsync(job, site.Config, cancellationToken)).Match(_ => Option<PreviewError>.None, error => error);
}
