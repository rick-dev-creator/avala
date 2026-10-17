using Avala.Documentation.Contracts;
using Avala.Workbench.Conversation;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Tests.Conversation;

public sealed class DocsLineViewModelScripts
{
    [Fact]
    public void AWrittenVersionShowsAsAQuietLineWithItsFileAndKind()
    {
        var line = new DocsLineViewModel(new DocsEntry("docs:2", "Refund webhooks", new FeatureVersion(2, VersionKind.Deviation, "docs/features/refund-webhooks/v2-deviation.md", DateTimeOffset.UnixEpoch)));

        Assert.Equal(("v2-deviation.md", "Refund webhooks · deviation", true), (line.File, line.Fact, line.IsDeviation));
    }
}
