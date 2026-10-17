using Avala.Documentation.Contracts;
using Avala.Testing;
using Avala.Workbench.Documenting;
using Avala.Workbench.Projects;
using Avala.Workbench.Sidebar;
using CommunityToolkit.Mvvm.Messaging;

namespace Avala.Workbench.Tests.Sidebar;

public sealed class ProjectDocsViewModelScripts
{
    private static readonly Feature Refunds = new("refund-webhooks", "Refund webhooks");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ARepositoryWithDocumentedFeaturesListsThemWithTheirVersionsAndOneWithoutShowsNoDocsAsync()
    {
        var documentation = new FakeDocumentation { Docs = new RepositoryDocs(true, "docs/features", [new RepositoryFeature(Refunds, 2)]) };
        var docs = new ProjectDocsViewModel(new ProjectShelf(new FakeProjectBook(), new RememberedLayout(), documentation, new StrongReferenceMessenger()));
        var none = new ProjectDocsViewModel(new ProjectShelf(new FakeProjectBook(), new RememberedLayout(), new FakeDocumentation(), new StrongReferenceMessenger()));

        await docs.ReadAsync("/repos/ledger", Cancellation);
        await none.ReadAsync("/repos/shop", Cancellation);

        Assert.Equal((true, 1), (docs.IsShown, docs.Count));
        Assert.Equal([("Refund webhooks", "2 versions")], docs.Features.Select(feature => (feature.Title, feature.Fact)));
        Assert.False(none.IsShown);
    }

    [Fact]
    public async Task OpeningAFeatureAsksForItsDocsAndTheToggleFoldsTheListAsync()
    {
        var messenger = new StrongReferenceMessenger();
        var asked = new List<FeatureDocsRequested>();
        messenger.Register<FeatureDocsRequested>(asked, (list, message) => ((List<FeatureDocsRequested>)list).Add(message));
        var docs = new ProjectDocsViewModel(new ProjectShelf(new FakeProjectBook(), new RememberedLayout(), new FakeDocumentation { Docs = new RepositoryDocs(true, "docs/features", [new RepositoryFeature(Refunds, 2)]) }, messenger));
        await docs.ReadAsync("/repos/ledger", Cancellation);

        docs.ToggleCommand.Execute(null);
        docs.Features[0].OpenCommand.Execute(null);

        Assert.True(docs.IsOpen);
        Assert.Equal([new FeatureDocsRequested("/repos/ledger", Refunds)], asked);
    }
}
