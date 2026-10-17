using Avala.Documentation.Catalog;
using Avala.Documentation.Contracts;
using Avala.Documentation.Features;
using Avala.Documentation.Records;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Documentation.Writing;

internal sealed class DocumentationEntry(IDocsRules rules, FeatureCatalog catalog, DocumentationBook book, IWorkingFiles files) : IDocumentation
{
    public async Task<RepositoryDocs> OfRepositoryAsync(string repository, CancellationToken cancellationToken)
    {
        var declared = (await rules.OfRepositoryAsync(repository, cancellationToken)).Match(found => found, _ => DocsRules.Off);

        return declared.Enabled ? new RepositoryDocs(true, declared.Folder, await catalog.ListAsync(repository, declared.Folder, cancellationToken)) : RepositoryDocs.Off with { Folder = declared.Folder };
    }

    public FeatureSuggestion Suggest(string instruction, IReadOnlyList<Feature> features) => FeatureNames.Suggest(instruction, features);

    public Task<Result<DocsRules, DocumentationError>> SwitchAsync(string repository, bool enabled, CancellationToken cancellationToken) =>
        rules.SwitchAsync(repository, enabled, cancellationToken);

    public async Task<Option<FeatureStory>> StoryAsync(string folder, Feature feature, CancellationToken cancellationToken)
    {
        var declared = (await rules.OfRepositoryAsync(folder, cancellationToken)).Match(found => found, _ => DocsRules.Off);
        var place = new FeatureFolder(declared.Folder, feature);
        var versions = await catalog.VersionsAsync(folder, place, cancellationToken);
        var played = new List<PlayedVersion>();
        var previous = Option<C4Diagram>.None;

        foreach (var version in versions)
        {
            var text = (await files.ReadAsync(folder, $"{place.Path}/{version.File}", cancellationToken)).Match(content => content.Match(found => found, () => string.Empty), _ => string.Empty);
            var document = VersionReader.Read(text);
            var shown = Storyboard.Play(version.Number, version.Kind, version.File, document, previous);
            played.Add(shown);
            previous = shown.Structure.Elements.Count > 0 ? shown.Structure : previous;
        }

        return played.Count == 0 ? Option<FeatureStory>.None : new FeatureStory(feature, played);
    }

    public async Task ChooseAsync(JobId job, FeatureChoice choice, CancellationToken cancellationToken) =>
        await book.ChooseAsync(job, choice, cancellationToken);

    public Option<JobDocumentation> OfJob(JobId job) => book.Of(job).Bind(documented => documented.Public);
}
