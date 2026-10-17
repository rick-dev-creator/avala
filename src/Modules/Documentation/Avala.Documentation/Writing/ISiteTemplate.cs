using Avala.Documentation.Features;

namespace Avala.Documentation.Writing;

internal interface ISiteTemplate
{
    IReadOnlyList<SiteFile> Files { get; }
}
