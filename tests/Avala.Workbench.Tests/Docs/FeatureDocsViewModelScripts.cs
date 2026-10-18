using Avala.Documentation.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workbench.Docs;
using Avala.Workbench.Documenting;
using Avala.Workbench.Navigation;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Workbench.Tests.Docs;

public sealed class FeatureDocsViewModelScripts
{
    [Fact]
    public async Task AskingForAFeaturesDocsLoadsItsStoryIntoThePlayerAndShowsThePage()
    {
        var messenger = new StrongReferenceMessenger();
        var shown = new List<Sdk.Presentation.PageRequested>();
        messenger.Register<Sdk.Presentation.PageRequested>(shown, (list, message) => ((List<Sdk.Presentation.PageRequested>)list).Add(message));
        var documentation = new FakeDocumentation { Story = DesignStories.Ledger() };
        using var player = new C4PlayerViewModel(new FakeTimeProvider(), new InlineUiDispatcher(), new Avala.Components.Zooming.ZoomViewModel());
        var page = new FeatureDocsViewModel(documentation, new JobFocus(new TestRegions(), messenger), messenger, player);

        messenger.Send(new FeatureDocsRequested("/home/ada/code/ledger-api", new Feature("payments", "Payments on stripe-go v79")));
        await page.Loading;

        Assert.Equal(("Payments on stripe-go v79", "ledger-api · payments"), (page.Feature, page.Where));
        Assert.Equal(["v1 Proposed", "v2 Deviation"], page.Player.Versions);
        Assert.Same(page, Assert.Single(shown).Page);
    }
}
