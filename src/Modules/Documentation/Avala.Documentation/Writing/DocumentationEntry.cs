using Avala.Documentation.Contracts;
using Avala.Documentation.Features;
using Avala.Documentation.Records;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Documentation.Writing;

internal sealed class DocumentationEntry(IDocsRules rules, DocumentationBook book, DocumentationReads reads) : IDocumentation
{
    public Task<RepositoryDocs> OfRepositoryAsync(string repository, CancellationToken cancellationToken) => reads.RepositoryAsync(repository, cancellationToken);

    public FeatureSuggestion Suggest(string instruction, IReadOnlyList<Feature> features) => FeatureNames.Suggest(instruction, features);

    public Task<Result<DocsRules, DocumentationError>> SwitchAsync(string repository, bool enabled, CancellationToken cancellationToken) =>
        rules.SwitchAsync(repository, enabled, cancellationToken);

    public Task<Option<FeatureStory>> StoryAsync(string folder, Feature feature, CancellationToken cancellationToken) => reads.StoryAsync(folder, feature, cancellationToken);

    public async Task<Option<JobDocs>> JobDocsAsync(JobId job, CancellationToken cancellationToken) =>
        await OfJob(job).Match(found => reads.JobAsync(found, cancellationToken), () => Task.FromResult(Option<JobDocs>.None));

    public async Task ChooseAsync(JobId job, FeatureChoice choice, CancellationToken cancellationToken) =>
        await book.ChooseAsync(job, choice, cancellationToken);

    public Option<JobDocumentation> OfJob(JobId job) => book.Of(job).Bind(documented => documented.Public);
}
