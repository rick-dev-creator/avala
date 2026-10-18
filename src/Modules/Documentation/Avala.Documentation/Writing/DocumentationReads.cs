using Avala.Documentation.Catalog;
using Avala.Documentation.Contracts;
using Avala.Documentation.Features;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Documentation.Writing;

internal sealed record Played(Option<FeatureStory> Story, IReadOnlyList<UnplayedVersion> Unplayed);

internal sealed class DocumentationReads(IDocsRules rules, FeatureCatalog catalog, IWorkingFiles files, JobPlaces places)
{
    public async Task<RepositoryDocs> RepositoryAsync(string repository, CancellationToken cancellationToken)
    {
        var declared = (await rules.OfRepositoryAsync(repository, cancellationToken)).Match(found => found, _ => DocsRules.Off);

        if (!declared.Enabled)
        {
            return RepositoryDocs.Off with { Folder = declared.Folder };
        }

        var merged = await catalog.ListAsync(repository, declared.Folder, cancellationToken);

        return new RepositoryDocs(true, declared.Folder, [.. merged, .. await UnmergedAsync(repository, declared.Folder, merged, cancellationToken)]);
    }

    private async Task<IReadOnlyList<RepositoryFeature>> UnmergedAsync(string repository, string folder, IReadOnlyList<RepositoryFeature> merged, CancellationToken cancellationToken)
    {
        var listed = merged.Select(found => found.Feature.Slug).ToHashSet(StringComparer.Ordinal);
        var unmerged = new List<RepositoryFeature>();

        foreach (var place in (await places.OfRepositoryAsync(repository, cancellationToken)).Where(found => found.Status is not (JobStatus.Discarded or JobStatus.Failed)))
        {
            foreach (var feature in (await catalog.ListAsync(place.Worktree, folder, cancellationToken)).Where(found => listed.Add(found.Feature.Slug)))
            {
                unmerged.Add(feature with { Branch = place.Branch, Folder = place.Worktree });
            }
        }

        return unmerged;
    }

    public async Task<Option<FeatureStory>> StoryAsync(string folder, Feature feature, CancellationToken cancellationToken) =>
        (await PlayAsync(folder, feature, cancellationToken)).Story;

    private async Task<Played> PlayAsync(string folder, Feature feature, CancellationToken cancellationToken)
    {
        var declared = (await rules.OfRepositoryAsync(folder, cancellationToken)).Match(found => found, _ => DocsRules.Off);
        var place = new FeatureFolder(declared.Folder, feature);
        var versions = await catalog.VersionsAsync(folder, place, cancellationToken);
        var played = new List<PlayedVersion>();
        var unplayed = new List<UnplayedVersion>();
        var previous = Option<(C4Diagram Structure, string File)>.None;

        foreach (var version in versions)
        {
            if (!(await files.ReadAsync(folder, $"{place.Path}/{version.File}", cancellationToken)).TryGetValue(out var content, out _))
            {
                unplayed.Add(new UnplayedVersion(version.File) { After = previous.Map(before => before.File) });
                continue;
            }

            try
            {
                var text = content.Match(found => found, () => string.Empty);
                var shown = Storyboard.Play(version.Number, version.Kind, version.File, VersionReader.Read(text), previous.Map(before => before.Structure));

                played.Add(shown);
                previous = shown.Structure.Elements.Count > 0 ? (shown.Structure, version.File) : previous;
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                unplayed.Add(new UnplayedVersion(version.File) { After = previous.Map(before => before.File) });
                previous = Option<(C4Diagram Structure, string File)>.None;
            }
        }

        return new Played(played.Count == 0 ? Option<FeatureStory>.None : new FeatureStory(feature, played), unplayed);
    }

    public async Task<Option<JobDocs>> JobAsync(JobDocumentation documentation, Func<string, CancellationToken, Task<DocsSite>> site, CancellationToken cancellationToken) =>
        await (await places.OfAsync(documentation.Job, cancellationToken)).Match(
            async place => Option<JobDocs>.Some(await JobAsync(documentation, place, await PlayAsync(place.Worktree, documentation.Feature, cancellationToken), site, cancellationToken)),
            () => Task.FromResult(Option<JobDocs>.None));

    private async Task<JobDocs> JobAsync(JobDocumentation documentation, JobPlace place, Played played, Func<string, CancellationToken, Task<DocsSite>> site, CancellationToken cancellationToken) =>
        new(documentation, place.Repository, played.Story, Others(await RepositoryAsync(place.Repository, cancellationToken), documentation.Feature))
        {
            Site = await site(place.Worktree, cancellationToken),
            Unplayed = played.Unplayed,
            Unplanned = !documentation.Planned && place.Status is JobStatus.AwaitingReview or JobStatus.NeedsHelp or JobStatus.Approved or JobStatus.Discarded or JobStatus.Failed,
        };

    private static RepositoryDocs Others(RepositoryDocs docs, Feature feature) =>
        docs with { Features = [.. docs.Features.Where(listed => listed.Feature.Slug != feature.Slug || listed.Branch.IsNone)] };
}
