using System.Collections.Immutable;
using Avala.Documentation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workbench.Board;
using Avala.Workbench.Docs;
using Avala.Workbench.Documenting;
using Avala.Workbench.Following;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Workbench.Tests.Docs;

public sealed class DocsPaneViewModelScripts : IDisposable
{
    private static readonly Feature Payments = new("payments", "Payments on stripe-go v79");
    private static readonly Feature Payouts = new("payouts", "Payout scheduler");

    private readonly JobId job = JobId.New();
    private readonly JobBoard board = new();
    private readonly TestUiDispatcher ui = new();
    private readonly StrongReferenceMessenger messenger = new();
    private readonly FakeDocumentation documentation = new();
    private readonly FakeSitePreviews previews = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OpenedFromItsGlyphThePaneShowsTheJobsFeatureItsStoryItsChangesAndTheRepositorysOtherDocsAsync()
    {
        Document(writing: true);
        using var pane = Pane();
        using var toggle = Toggle();

        await ui.InvokeAsync(() => toggle.ToggleCommand.Execute(null), Cancellation);
        await ui.PresentedAsync(pane, () => pane.Feature.Length > 0, () => $"feature '{pane.Feature}'");

        var shown = await ui.ReadAsync(() => (pane.IsOpen, toggle.IsOpen, pane.Feature, pane.State, pane.IsWriting, pane.Player.Versions.Count, pane.History.Steps.Select(step => step.Title).ToList(), pane.Others.Select(other => other.Title).ToList()));
        Assert.Equal((true, true, "Payments on stripe-go v79", "writing", true, 2), (shown.Item1, shown.Item2, shown.Item3, shown.Item4, shown.Item5, shown.Item6));
        Assert.Equal(["Plan v1 · proposed", "Change request #1", "Plan v2 · deviation"], shown.Item7);
        Assert.Equal(["Payout scheduler"], shown.Item8);
    }

    [Fact]
    public async Task TheGlyphShowsOnlyForADocumentedJobAndOpeningAnotherFeatureAsksForItsPageAsync()
    {
        using var toggle = Toggle();
        toggle.OnRegionContextChanged(job);
        toggle.Activate();
        await ui.PresentedAsync(toggle, () => toggle.Revision > 0, () => "no revision");
        var hidden = await ui.ReadAsync(() => toggle.IsShown);
        Document(writing: false);
        await ui.PresentedAsync(toggle, () => toggle.IsShown, () => "hidden");
        using var pane = Pane();
        var asked = new List<FeatureDocsRequested>();
        messenger.Register<FeatureDocsRequested>(asked, (list, message) => ((List<FeatureDocsRequested>)list).Add(message));

        await ui.InvokeAsync(() => messenger.Send(new DocsPaneRequested(job)), Cancellation);
        await ui.PresentedAsync(pane, () => pane.Others.Count == 1, () => $"{pane.Others.Count} others");
        await ui.InvokeAsync(() => pane.Others[0].OpenCommand.Execute(null), Cancellation);

        Assert.False(hidden);
        Assert.Equal([new FeatureDocsRequested("/repos/ledger", Payouts)], asked);
    }

    [Fact]
    public async Task ADocsSiteOpensInThePreviewAndWithoutDocfxThePaneSaysHowToInstallItAsync()
    {
        Document(writing: false, site: new DocsSite(true, "docs", true));
        using var pane = Pane();
        await ui.InvokeAsync(() => messenger.Send(new DocsPaneRequested(job)), Cancellation);
        await ui.PresentedAsync(pane, () => pane.CanOpenSite, () => "cannot open");

        await ui.RunAsync(() => pane.OpenSiteCommand.ExecuteAsync(null));
        Document(writing: false, site: new DocsSite(true, "docs", false));
        await ui.PresentedAsync(pane, () => !pane.CanOpenSite, () => "can open");

        Assert.Equal([(job, "docs/docfx.json")], previews.Served);
        Assert.Equal(DocsPanePhrases.Install, await ui.ReadAsync(() => pane.SiteHint));
    }

    public void Dispose() => ui.Dispose();

    [Fact]
    public async Task ChoosingAVersionInTheHistorySummarizesItAndPlaysItsC4Async()
    {
        Document(writing: false);
        using var pane = Pane();
        await ui.InvokeAsync(() => messenger.Send(new DocsPaneRequested(job)), Cancellation);
        await ui.PresentedAsync(pane, () => pane.History.Steps.Count == 3, () => $"{pane.History.Steps.Count} steps");

        await ui.InvokeAsync(() => pane.History.Steps[0].ChooseCommand.Execute(null), Cancellation);

        var shown = await ui.ReadAsync(() => (pane.History.Heading, pane.History.Why, pane.Player.Version, pane.History.Steps[0].IsChosen));
        Assert.Equal(("Plan v1 · proposed", "None. It is the plan the agent proposed.", 0, true), shown);
    }

    private void Document(bool writing, DocsSite? site = null)
    {
        var documented = new JobDocumentation(job, Payments)
        {
            Writing = writing ? JobId.New() : Option<JobId>.None,
            Versions = [new FeatureVersion(1, VersionKind.Proposed, "docs/features/payments/v1-proposed.md", DateTimeOffset.UnixEpoch), new FeatureVersion(2, VersionKind.Deviation, "docs/features/payments/v2-deviation.md", DateTimeOffset.UnixEpoch)],
            Requests = [new ChangeRequest(1, RequestKind.Steer, "Make sure a refund never applies twice", DateTimeOffset.UnixEpoch, 1) { Version = 2 }],
        };
        documentation.Job = new JobDocs(documented, "/repos/ledger", DesignStories.Ledger(), new RepositoryDocs(true, "docs/features", [new RepositoryFeature(Payments, 2), new RepositoryFeature(Payouts, 1)])) { Site = site ?? new DocsSite(false, "docs", true) };
        board.Publish(ImmutableDictionary<JobId, JobDocumentation>.Empty.Add(job, documented));
    }

    private DocsPaneViewModel Pane()
    {
        var pane = new DocsPaneViewModel(new DocsReader(board, documentation, previews), new LiveFeed(new Pulse(board), ui), messenger, new C4PlayerViewModel(new FakeTimeProvider(), new InlineUiDispatcher()));
        pane.OnRegionContextChanged(job);
        pane.Activate();

        return pane;
    }

    private DocsToggleViewModel Toggle()
    {
        var toggle = new DocsToggleViewModel(new DocsReader(board, documentation, previews), new LiveFeed(new Pulse(board), ui), messenger);
        toggle.OnRegionContextChanged(job);

        return toggle;
    }
}
