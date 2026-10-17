using Avala.Documentation.Features;
using Avala.Documentation.Writing;

namespace Avala.Documentation.RepositoryFiles;

internal sealed class EmbeddedSiteTemplate : ISiteTemplate
{
    public const string Prefix = "SiteTemplate/";

    private readonly Lazy<IReadOnlyList<SiteFile>> files = new(Read);

    public IReadOnlyList<SiteFile> Files => files.Value;

    private static IReadOnlyList<SiteFile> Read()
    {
        var assembly = typeof(EmbeddedSiteTemplate).Assembly;

        return
        [
            .. assembly.GetManifestResourceNames()
                .Where(name => name.StartsWith(Prefix, StringComparison.Ordinal))
                .Order(StringComparer.Ordinal)
                .Select(name =>
                {
                    using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);

                    return new SiteFile(name[Prefix.Length..].Replace('\\', '/'), reader.ReadToEnd().ReplaceLineEndings("\n"));
                }),
        ];
    }
}
