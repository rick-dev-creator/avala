using Avala.Documentation.Contracts;
using Avala.Documentation.Features;
using Avala.Workspaces.Contracts;

namespace Avala.Documentation.Catalog;

internal sealed class FeatureCatalog(IRepositoryIndex index, IWorkingFiles files)
{
    public const int FileLimit = 50_000;

    public async Task<IReadOnlyList<RepositoryFeature>> ListAsync(string folder, string root, CancellationToken cancellationToken)
    {
        var listed = await index.FilesAsync(folder, FileLimit, cancellationToken);
        var features = new List<RepositoryFeature>();

        foreach (var slug in listed.Select(file => SlugOf(file, root)).OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var readme = await files.ReadAsync(folder, $"{root}/{slug}/README.md", cancellationToken);
            var title = readme.Match(content => content.Bind(FeatureFolder.TitleIn), _ => Sdk.Option<string>.None).Match(found => found, () => slug);
            var feature = new Feature(slug, title);
            features.Add(new RepositoryFeature(feature, new FeatureFolder(root, feature).VersionsIn(listed).Count));
        }

        return features;
    }

    public async Task<IReadOnlyList<VersionFile>> VersionsAsync(string folder, FeatureFolder feature, CancellationToken cancellationToken) =>
        feature.VersionsIn(await index.FilesAsync(folder, FileLimit, cancellationToken));

    private static string? SlugOf(string file, string root)
    {
        var parts = file.StartsWith(root + "/", StringComparison.Ordinal) ? file[(root.Length + 1)..].Split('/') : [];

        return parts is [var slug, "README.md"] ? slug : null;
    }
}
