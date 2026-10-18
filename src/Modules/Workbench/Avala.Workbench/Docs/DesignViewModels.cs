using Avala.Components.Graphs;
using Avala.Components.Zooming;
using Avala.Documentation.Contracts;
using Avala.Sdk;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Docs;

internal static class DesignStories
{
    private static readonly C4Element Merchant = new("merchant", "Merchant", C4Shape.Person, false);
    private static readonly C4Element Api = new("api", "Ledger API", C4Shape.Container, false) { Technology = "Go · HTTP", Boundary = "ledger" };
    private static readonly C4Element Payments = new("payments", "Payments", C4Shape.Container, false) { Technology = "Go · stripe-go v79", Boundary = "ledger" };
    private static readonly C4Element Postgres = new("postgres", "Ledger DB", C4Shape.Database, false) { Technology = "Postgres", Boundary = "ledger" };
    private static readonly C4Element Stripe = new("stripe", "Stripe", C4Shape.System, true) { Description = "API 2026-09" };
    private static readonly C4Element Webhooks = new("webhooks", "Webhooks", C4Shape.Container, false) { Technology = "Go · signature check", Boundary = "ledger" };
    private static readonly C4Relation Charges = new("merchant", "api", "Charges, refunds");
    private static readonly C4Relation Calls = new("api", "payments", "Calls");
    private static readonly C4Relation ToStripe = new("payments", "stripe", "stripe-go v79");
    private static readonly C4Relation Writes = new("payments", "postgres", "Writes ledger");
    private static readonly C4Relation Events = new("stripe", "webhooks", "Events");
    private static readonly C4Relation Verified = new("webhooks", "payments", "Verified events");

    public static FeatureStory Ledger()
    {
        C4Boundary[] boundary = [new("ledger", "ledger-api")];
        var first = new C4Diagram(C4Level.Container, "Payments on stripe-go v79", [Merchant, Api, Payments, Stripe, Postgres], boundary, [Charges, Calls, ToStripe, Writes]);
        var second = first with { Elements = [Merchant, Api, Payments with { Description = "Checks a key first" }, Stripe, Postgres, Webhooks], Relations = [Charges, Calls, ToStripe, Writes, Events, Verified] };

        return new FeatureStory(
            new Feature("payments-on-stripe-go-v79", "Payments on stripe-go v79"),
            [
                new PlayedVersion(1, VersionKind.Proposed, "v1-proposed.md", new VersionDocument("Payments on stripe-go v79", "What the orchestrator proposed when its plan was approved: move every Stripe call to the v79 client behind the Payments container.", [], []), first,
                [
                    new PlayStep(StepKind.Base, "merchant", "Merchant", "Merchants create charges and refunds through the Ledger API. Nothing changes for them.") { Elements = ["merchant"] },
                    new PlayStep(StepKind.Base, "api", "Ledger API", "The HTTP API keeps its contract and goes through Payments.") { Elements = ["api"], Relations = [Charges] },
                    new PlayStep(StepKind.Base, "payments", "Payments", "A new container owns every call to Stripe, on the stripe-go v79 client.") { Elements = ["payments"], Relations = [Calls] },
                    new PlayStep(StepKind.Base, "stripe", "Stripe", "v79 targets the API version 2026-09.") { Elements = ["stripe"], Relations = [ToStripe] },
                    new PlayStep(StepKind.Base, "postgres", "Ledger DB", "Payments writes every charge and refund to the ledger.") { Elements = ["postgres"], Relations = [Writes] },
                    new PlayStep(StepKind.Flow, "a-charge", "Flow: a charge", "A charge crosses the system in four hops.") { Relations = [Charges with { Label = "The merchant asks for a charge" }, Calls with { Label = "The API hands it to Payments" }, ToStripe with { Label = "Payments charges through stripe-go v79" }, Writes with { Label = "The ledger entry is written" }] },
                ]),
                new PlayedVersion(2, VersionKind.Deviation, "v2-deviation.md", new VersionDocument("Payments on stripe-go v79", "The plan did not name webhooks, but v79 changes how they are signed.", [], []), second,
                [
                    new PlayStep(StepKind.Added, "webhooks", "Webhooks", "Not in v1. Stripe events arrive in their own container, which checks the v79 signature.") { Elements = ["webhooks"], Relations = [Events, Verified] },
                    new PlayStep(StepKind.Changed, "payments", "Payments", "Payments checks a key before each charge and refund.") { Elements = ["payments"] },
                ]),
            ]);
    }
}

internal sealed class DesignC4PlayerViewModel : IC4PlayerViewModel, IDisposable
{
    private readonly C4PlayerViewModel player = new(TimeProvider.System, new DesignDispatcher(), new ZoomViewModel());

    public DesignC4PlayerViewModel() => player.Show(DesignStories.Ledger());

    public void Dispose() => player.Dispose();

    public bool IsShown => player.IsShown;

    public bool IsCompact { get; init; }

    public string Title => player.Title;

    public string Lead => player.Lead;

    public string Path => player.Path;

    public IReadOnlyList<string> Versions => player.Versions;

    public int Version
    {
        get => player.Version;
        set => player.Version = value;
    }

    public IReadOnlyList<IC4NodeViewModel> Nodes => player.Nodes;

    public IReadOnlyList<GraphLink> Links => player.Links;

    public IReadOnlyList<GraphFrame> Frames => player.Frames;

    public IReadOnlyList<GraphLink> Structure => player.Structure;

    public IReadOnlyList<IDocSectionViewModel> Sections => player.Sections;

    public IReadOnlyList<Conversation.ICanvasViewModel> Unread => player.Unread;

    public bool IsDrawn => player.IsDrawn;

    public string Caption => player.Caption;

    public string Position => player.Position;

    public string Speed => player.Speed;

    public bool IsPlaying => player.IsPlaying;

    public IRelayCommand PlayCommand => player.PlayCommand;

    public IRelayCommand StopCommand => player.StopCommand;

    public IRelayCommand BackCommand => player.BackCommand;

    public IRelayCommand NextCommand => player.NextCommand;

    public IRelayCommand SpeedCommand => player.SpeedCommand;

    public IZoomViewModel Zoom => player.Zoom;

    private sealed class DesignDispatcher : IUiDispatcher
    {
        public ValueTask InvokeAsync(Action action, CancellationToken cancellationToken)
        {
            action();

            return ValueTask.CompletedTask;
        }
    }
}

internal sealed record DesignC4NodeViewModel(string Key, string Label, string Kind, string Technology) : IC4NodeViewModel
{
    public DesignC4NodeViewModel()
        : this("payments", "Payments", "Container", "Go · stripe-go v79")
    {
    }

    public string Frame => string.Empty;

    public bool IsPerson => false;

    public bool IsExternal => false;

    public bool IsDatabase => false;

    public bool IsHidden => false;

    public bool IsNew { get; init; } = true;

    public bool IsChanged => false;

    public bool IsRemoved => false;

    public bool IsFocus { get; init; } = true;

    public bool IsReached => false;
}

internal sealed record DesignDocSectionViewModel(string Anchor, string Title, string Text, string Mark) : IDocSectionViewModel
{
    public DesignDocSectionViewModel()
        : this("payments", "Payments", "A new container owns every call to Stripe, on the stripe-go v79 client.", "new")
    {
    }

    public bool IsNew => Mark == "new";

    public bool IsChanged => Mark == "changed";

    public bool IsRemoved => Mark == "removed";

    public bool IsFlow => Mark == "flow";

    public bool IsActive { get; init; } = true;

    public IRelayCommand ChooseCommand { get; } = new RelayCommand(() => { });
}

internal sealed class DesignFeatureDocsViewModel : IFeatureDocsViewModel, IDisposable
{
    private readonly DesignC4PlayerViewModel player = new();

    public string Title => "Docs";

    public string Feature => "Payments on stripe-go v79";

    public string Where => "ledger-api · payments-on-stripe-go-v79";

    public IC4PlayerViewModel Player => player;

    public void Dispose() => player.Dispose();
}

internal sealed class DesignDocsToggleViewModel : IDocsToggleViewModel
{
    public bool IsShown => true;

    public bool IsOpen { get; init; } = true;

    public IRelayCommand ToggleCommand { get; } = new RelayCommand(() => { });
}

internal sealed class DesignDocsPaneViewModel : IDocsPaneViewModel, IDisposable
{
    private readonly DesignC4PlayerViewModel player = new() { IsCompact = true };

    public bool IsOpen => true;

    public string Feature => "Payments on stripe-go v79";

    public string State => "writing";

    public bool IsWriting => true;

    public IC4PlayerViewModel Player => player;

    public IJobHistoryViewModel History { get; } = new DesignJobHistoryViewModel();

    public bool CanOpenSite => true;

    public string SiteHint => string.Empty;

    public IAsyncRelayCommand OpenSiteCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IReadOnlyList<Sidebar.IFeatureRowViewModel> Others { get; } = [new Sidebar.DesignFeatureRowViewModel("Payout scheduler", "1 version")];

    public IRelayCommand CloseCommand { get; } = new RelayCommand(() => { });

    public void Dispose() => player.Dispose();
}

internal sealed class DesignJobHistoryViewModel : IJobHistoryViewModel
{
    public bool HasSteps => true;

    public IReadOnlyList<IHistoryStepViewModel> Steps { get; } =
    [
        new DesignHistoryStepViewModel("Plan v1 · proposed", "Oct 17, 10:02", true, false),
        new DesignHistoryStepViewModel("Change request #1", "“Make sure a refund never applies twice”", false, false),
        new DesignHistoryStepViewModel("Plan v2 · deviation", "Oct 17, 10:40", true, true),
    ];

    public string Heading => "Plan v2 · deviation";

    public string Happened => "The agent added an idempotency store between Payments and the ledger.";

    public string Why => "You asked “Make sure a refund never applies twice”. The plan changed to do it.";

    public IReadOnlyList<string> Changed { get; } = ["+ Idempotency store", "~ Payments"];

    public string Records => "v2-deviation.md · plans/v2.md";

    public bool HasWriter => true;

    public IRelayCommand OpenWriterCommand { get; } = new RelayCommand(() => { });
}

internal sealed record DesignHistoryStepViewModel(string Title, string Fact, bool IsVersion, bool IsChosen) : IHistoryStepViewModel
{
    public DesignHistoryStepViewModel()
        : this("Plan v2 · deviation", "Oct 17, 10:40", true, true)
    {
    }

    public IRelayCommand ChooseCommand { get; } = new RelayCommand(() => { });
}
