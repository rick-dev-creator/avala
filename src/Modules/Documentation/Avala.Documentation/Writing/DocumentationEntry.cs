using Avala.Documentation.Catalog;
using Avala.Documentation.Contracts;
using Avala.Documentation.Features;
using Avala.Documentation.Records;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Documentation.Writing;

internal sealed class DocumentationEntry(IDocsRules rules, FeatureCatalog catalog, DocumentationBook book) : IDocumentation
{
    public async Task<RepositoryDocs> OfRepositoryAsync(string repository, CancellationToken cancellationToken)
    {
        var declared = (await rules.OfRepositoryAsync(repository, cancellationToken)).Match(found => found, _ => DocsRules.Off);

        return declared.Enabled ? new RepositoryDocs(true, declared.Folder, await catalog.ListAsync(repository, declared.Folder, cancellationToken)) : RepositoryDocs.Off with { Folder = declared.Folder };
    }

    public FeatureSuggestion Suggest(string instruction, IReadOnlyList<Feature> features) => FeatureNames.Suggest(instruction, features);

    public Task<Result<DocsRules, DocumentationError>> SwitchAsync(string repository, bool enabled, CancellationToken cancellationToken) =>
        rules.SwitchAsync(repository, enabled, cancellationToken);

    public async Task ChooseAsync(JobId job, FeatureChoice choice, CancellationToken cancellationToken) =>
        await book.ChooseAsync(job, choice, cancellationToken);

    public Option<JobDocumentation> OfJob(JobId job) => book.Of(job).Bind(documented => documented.Public);
}
