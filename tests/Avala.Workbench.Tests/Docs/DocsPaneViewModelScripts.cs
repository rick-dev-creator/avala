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
    private readonly JobId writer = JobId.New();
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

    [Fact]
    public async Task ADocsReadThatFailsMakesThePaneSaySoAndTheNextReadShowsTheFeatureAgainAsync()
    {
        Document(writing: false);
        documentation.Unreadable = "The section Webhooks is repeated";
        using var pane = Pane();
        await ui.InvokeAsync(() => messenger.Send(new DocsPaneRequested(job)), Cancellation);
        await ui.PresentedAsync(pane, () => pane.State.Length > 0, () => "no state");
        var failed = await ui.ReadAsync(() => (pane.State, pane.Problem));

        documentation.Unreadable = Option<string>.None;
        Document(writing: true);

        await ui.PresentedAsync(pane, () => pane.State == "writing", () => $"state '{pane.State}'");
        Assert.Equal(("unreadable", "Could not read this feature's docs: The section Webhooks is repeated"), failed);
        Assert.Equal(string.Empty, await ui.ReadAsync(() => pane.Problem));
    }

    [Fact]
    public async Task AnotherJobWhoseDocsReadFailsShowsNothingOfTheJobShownBeforeAsync()
    {
        Document(writing: false);
        var other = JobId.New();
        board.Publish(board.Docs.Add(other, new JobDocumentation(other, Payouts)));
        using var pane = Pane();
        await ui.InvokeAsync(() => messenger.Send(new DocsPaneRequested(job)), Cancellation);
        await ui.PresentedAsync(pane, () => pane.Feature.Length > 0, () => "no feature");
        documentation.Unreadable = "The section Webhooks is repeated";

        await ui.InvokeAsync(() => pane.OnRegionContextChanged(other), Cancellation);
        await ui.PresentedAsync(pane, () => pane.State == DocsPanePhrases.Unreadable, () => $"state '{pane.State}'");

        var shown = await ui.ReadAsync(() => (pane.IsOpen, pane.Feature, pane.Player.IsShown, pane.History.Steps.Count, pane.Others.Count, pane.CanOpenSite));
        Assert.Equal((true, "Payout scheduler", false, 0, 0, false), shown);
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

    [Fact]
    public async Task AnotherJobsHistoryOpensOnItsLatestVersionNotOnTheVersionChosenInTheJobShownBeforeAsync()
    {
        Document(writing: false);
        using var pane = Pane();
        await ui.InvokeAsync(() => messenger.Send(new DocsPaneRequested(job)), Cancellation);
        await ui.PresentedAsync(pane, () => pane.History.Steps.Count == 3, () => $"{pane.History.Steps.Count} steps");
        await ui.InvokeAsync(() => pane.History.Steps[0].ChooseCommand.Execute(null), Cancellation);
        var other = JobId.New();
        var payouts = new JobDocumentation(other, Payouts)
        {
            Versions = [new FeatureVersion(1, VersionKind.Proposed, "docs/features/payouts/v1-proposed.md", DateTimeOffset.UnixEpoch), new FeatureVersion(2, VersionKind.Deviation, "docs/features/payouts/v2-deviation.md", DateTimeOffset.UnixEpoch)],
        };
        documentation.Job = new JobDocs(payouts, "/repos/ledger", DesignStories.Ledger(), new RepositoryDocs(true, "docs/features", [new RepositoryFeature(Payments, 2), new RepositoryFeature(Payouts, 2)]));
        board.Publish(board.Docs.Add(other, payouts));

        await ui.InvokeAsync(() => pane.OnRegionContextChanged(other), Cancellation);
        await ui.PresentedAsync(pane, () => pane.Feature == Payouts.Title, () => $"feature '{pane.Feature}'");

        var shown = await ui.ReadAsync(() => (pane.History.Heading, pane.History.Steps.Single(step => step.IsChosen).Title));
        Assert.Equal(("Plan v2 · deviation", "Plan v2 · deviation"), shown);
    }

    [Fact]
    public async Task AVersionsWriterOpensFromTheHistoryInItsOwnConversationAsync()
    {
        Document(writing: false);
        var opened = new List<JobId>();
        messenger.Register<Workbench.Contracts.Presentation.JobSelected>(this, (_, selected) => opened.Add(selected.Job));
        using var pane = Pane();
        await ui.InvokeAsync(() => messenger.Send(new DocsPaneRequested(job)), Cancellation);
        await ui.PresentedAsync(pane, () => pane.History.HasWriter, () => "no writer");

        await ui.InvokeAsync(() => pane.History.OpenWriterCommand.Execute(null), Cancellation);
        await ui.InvokeAsync(() => pane.History.Steps[0].ChooseCommand.Execute(null), Cancellation);

        Assert.Equal([writer], opened);
        Assert.False(await ui.ReadAsync(() => pane.History.HasWriter));
    }

    private void Document(bool writing, DocsSite? site = null)
    {
        var documented = new JobDocumentation(job, Payments)
        {
            Writing = writing ? JobId.New() : Option<JobId>.None,
            Versions = [new FeatureVersion(1, VersionKind.Proposed, "docs/features/payments/v1-proposed.md", DateTimeOffset.UnixEpoch), new FeatureVersion(2, VersionKind.Deviation, "docs/features/payments/v2-deviation.md", DateTimeOffset.UnixEpoch) { Writer = writer }],
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
