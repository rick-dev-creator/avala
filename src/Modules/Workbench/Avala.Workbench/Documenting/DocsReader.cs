using Avala.Documentation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Board;

namespace Avala.Workbench.Documenting;

internal sealed class DocsReader(JobBoard board, IDocumentation documentation)
{
    public bool Offers(JobId job) => board.Docs.ContainsKey(job);

    public Task<Option<JobDocs>> JobAsync(JobId job, CancellationToken cancellationToken) => documentation.JobDocsAsync(job, cancellationToken);
}
