using Avala.Documentation.Catalog;
using Avala.Documentation.Contracts;
using Avala.Documentation.Features;
using Avala.Documentation.Records;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Documentation.Writing;

internal sealed record Subject(JobPlace Place, DocumentedJob Documented, FeatureFolder Folder);

internal sealed class DocumentedJobs(JobPlaces places, IDocsRules rules, FeatureCatalog catalog, DocumentationBook book)
{
    public Task<Option<JobPlace>> PlaceAsync(JobId job, CancellationToken cancellationToken) => places.OfAsync(job, cancellationToken);

    public Task<Option<JobStatus>> StatusAsync(JobId job, CancellationToken cancellationToken) => places.StatusAsync(job, cancellationToken);

    public async Task<Option<Subject>> OfAsync(JobId job, CancellationToken cancellationToken) =>
        await (await places.OfAsync(job, cancellationToken)).Match(
            place => place.Parent.IsSome ? Task.FromResult(Option<Subject>.None) : DocumentedAsync(place, cancellationToken),
            () => Task.FromResult(Option<Subject>.None));

    private async Task<Option<Subject>> DocumentedAsync(JobPlace place, CancellationToken cancellationToken)
    {
        var declared = (await rules.OfWorktreeAsync(place.Worktree, cancellationToken)).Match(found => found, _ => DocsRules.Off);

        if (!declared.Enabled)
        {
            return Option<Subject>.None;
        }

        var documented = await book.Of(place.Job).Match(
            found => Task.FromResult(found),
            async () =>
            {
                var features = await catalog.ListAsync(place.Worktree, declared.Folder, cancellationToken);
                var suggested = FeatureNames.Suggest(place.Instruction, [.. features.Select(listed => listed.Feature)]);

                return await book.ChooseAsync(place.Job, FeatureChoice.Of(suggested.Feature), cancellationToken);
            });

        return documented.Choice.Feature.Map(feature => new Subject(place, documented, new FeatureFolder(declared.Folder, feature)));
    }
}
