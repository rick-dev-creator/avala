using Avala.Documentation.Contracts;
using Avala.Workbench.Docs;

namespace Avala.Workbench.Tests.Docs;

public sealed class HistoryStepViewModelScripts
{
    [Fact]
    public void AVersionStepCanBeChosenAndARequestStepQuotesWhatWasAsked()
    {
        var chosen = new List<int>();
        var version = HistoryStepViewModel.Version(new FeatureVersion(3, VersionKind.AsBuilt, "docs/features/payments/v3-as-built.md", DateTimeOffset.UnixEpoch), chosen.Add);
        var request = HistoryStepViewModel.Request(new ChangeRequest(2, RequestKind.SendBack, "Keep the keys", DateTimeOffset.UnixEpoch, 1));

        version.ChooseCommand.Execute(null);

        Assert.Equal(("v3 · as built", true), (version.Title, version.IsVersion));
        Assert.Equal(("Change request #2", "“Keep the keys”", false, false), (request.Title, request.Fact, request.IsVersion, request.ChooseCommand.CanExecute(null)));
        Assert.Equal([3], chosen);
    }
}
