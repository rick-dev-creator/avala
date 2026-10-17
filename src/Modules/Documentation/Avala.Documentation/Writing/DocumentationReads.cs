using Avala.Documentation.Catalog;
using Avala.Documentation.Contracts;
using Avala.Documentation.Features;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Documentation.Writing;

internal sealed class DocumentationReads(IDocsRules rules, FeatureCatalog catalog, IWorkingFiles files, JobPlaces places)
{
    public async Task<RepositoryDocs> RepositoryAsync(string repository, CancellationToken cancellationToken)
    {
        var declared = (await rules.OfRepositoryAsync(repository, cancellationToken)).Match(found => found, _ => DocsRules.Off);

        return declared.Enabled ? new RepositoryDocs(true, declared.Folder, await catalog.ListAsync(repository, declared.Folder, cancellationToken)) : RepositoryDocs.Off with { Folder = declared.Folder };
    }

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
            var shown = Storyboard.Play(version.Number, version.Kind, version.File, VersionReader.Read(text), previous);
            played.Add(shown);
            previous = shown.Structure.Elements.Count > 0 ? shown.Structure : previous;
        }

        return played.Count == 0 ? Option<FeatureStory>.None : new FeatureStory(feature, played);
    }

    public async Task<Option<JobDocs>> JobAsync(JobDocumentation documentation, Func<string, CancellationToken, Task<DocsSite>> site, CancellationToken cancellationToken) =>
        await (await places.OfAsync(documentation.Job, cancellationToken)).Match(
            async place => Option<JobDocs>.Some(new JobDocs(
                documentation,
                place.Repository,
                await StoryAsync(place.Worktree, documentation.Feature, cancellationToken),
                await RepositoryAsync(place.Repository, cancellationToken))
            {
                Site = await site(place.Worktree, cancellationToken),
            }),
            () => Task.FromResult(Option<JobDocs>.None));
}
