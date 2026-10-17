using Avala.Documentation.Catalog;
using Avala.Documentation.Contracts;
using Avala.Documentation.Features;
using Avala.Documentation.Records;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Documentation.Writing;

internal sealed class DocumentationEntry(IDocsRules rules, FeatureCatalog catalog, DocumentationBook book) : IDocumentation
{
    public async Task<FeatureOffer> OfferAsync(string repository, string instruction, CancellationToken cancellationToken)
    {
        var declared = (await rules.OfRepositoryAsync(repository, cancellationToken)).Match(found => found, _ => DocsRules.Off);
        var features = declared.Enabled ? await catalog.ListAsync(repository, declared.Folder, cancellationToken) : [];

        return new FeatureOffer(declared.Enabled, FeatureNames.Suggest(instruction, [.. features.Select(listed => listed.Feature)]), features);
    }

    public async Task ChooseAsync(JobId job, FeatureChoice choice, CancellationToken cancellationToken) =>
        await book.ChooseAsync(job, choice, cancellationToken);

    public Option<JobDocumentation> OfJob(JobId job) => book.Of(job).Bind(documented => documented.Public);
}
