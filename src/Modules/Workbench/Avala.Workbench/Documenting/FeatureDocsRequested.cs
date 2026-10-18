using Avala.Documentation.Contracts;

namespace Avala.Workbench.Documenting;

internal sealed record FeatureDocsRequested(string Repository, Feature Feature)
{
    public Sdk.Option<string> Folder { get; init; }

    public Sdk.Option<string> Branch { get; init; }

    public static FeatureDocsRequested Of(string repository, RepositoryFeature listed) =>
        new(repository, listed.Feature) { Folder = listed.Folder, Branch = listed.Branch };
}
