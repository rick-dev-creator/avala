using Avala.Documentation.Contracts;
using Avala.Workbench.Conversation;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Tests.Conversation;

public sealed class ChangeLineViewModelScripts
{
    [Fact]
    public void ARecordedChangeShowsItsNumberKindAndTheVersionItProduced()
    {
        var line = new ChangeLineViewModel(new ChangeEntry("change:1", new ChangeRequest(1, RequestKind.SendBack, "Keep the keys in the ledger", DateTimeOffset.UnixEpoch, 2)));
        var linked = new ChangeLineViewModel(new ChangeEntry("change:2", new ChangeRequest(2, RequestKind.Steer, "Once only", DateTimeOffset.UnixEpoch, 1) { Version = 3 }));

        Assert.Equal(("Change request #1 recorded", "sent back"), (line.Text, line.Fact));
        Assert.Equal("steer → v3", linked.Fact);
    }
}
