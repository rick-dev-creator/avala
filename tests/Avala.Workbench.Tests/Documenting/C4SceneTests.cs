using Avala.Components.Graphs;
using Avala.Documentation.Contracts;
using Avala.Workbench.Documenting;

namespace Avala.Workbench.Tests.Documenting;

public sealed class C4SceneTests
{
    [Fact]
    public void BeforeTheFirstStepNothingOfAFirstVersionShowsAndEachStepRevealsItsElementAndRelations()
    {
        var story = Stories.Ledger();

        var start = C4Scene.Of(story, 0, -1, 0);
        var second = C4Scene.Of(story, 0, 1, 0);

        Assert.All(start.Nodes, node => Assert.Equal(NodeState.Hidden, node.State));
        Assert.Equal([("merchant", NodeState.Shown, false), ("payments", NodeState.Shown, true), ("postgres", NodeState.Hidden, false)], second.Nodes.Select(node => (node.Element.Alias, node.State, node.Focus)));
        Assert.Equal([("merchant", "payments", EdgeKind.Flowing)], second.Links.Select(link => (link.From, link.To, link.Kind)));
        Assert.Equal([new GraphFrame("ledger", "ledger-api")], second.Frames);
    }

    [Fact]
    public void AFlowStepNumbersItsRelationsHopByHopAndLightsTheElementsItReaches()
    {
        var story = Stories.Ledger();

        var hop = C4Scene.Of(story, 0, 3, 0);
        var both = C4Scene.Of(story, 0, 3, 1);

        Assert.Equal([("merchant", "payments", "1")], hop.Links.Select(link => (link.From, link.To, link.Badge)));
        Assert.Equal("1 · Asks for a charge", hop.Caption);
        Assert.Equal(["merchant", "payments"], hop.Nodes.Where(node => node.Reached).Select(node => node.Element.Alias));
        Assert.Equal(["1", "2"], both.Links.Select(link => link.Badge));
        Assert.Equal("2 · Writes the ledger", both.Caption);
    }

    [Fact]
    public void ALaterVersionStartsFromTheOneBeforeAndMarksWhatIsNewChangedAndRemoved()
    {
        var story = Stories.Ledger();

        var start = C4Scene.Of(story, 1, -1, 0);
        var end = C4Scene.Of(story, 1, 2, 0);

        Assert.Equal([("merchant", NodeState.Shown), ("payments", NodeState.Shown), ("webhooks", NodeState.Hidden), ("postgres", NodeState.Shown)], start.Nodes.Select(node => (node.Element.Alias, node.State)));
        Assert.Equal([("merchant", NodeState.Shown), ("payments", NodeState.Changed), ("webhooks", NodeState.New), ("postgres", NodeState.Removed)], end.Nodes.Select(node => (node.Element.Alias, node.State)));
        Assert.DoesNotContain(end.Links, link => link.To == "postgres" && link.Kind != EdgeKind.Waiting);
    }
}

internal static class Stories
{
    private static readonly C4Element Merchant = new("merchant", "Merchant", C4Shape.Person, false);
    private static readonly C4Element Payments = new("payments", "Payments", C4Shape.Container, false) { Technology = "Go", Boundary = "ledger" };
    private static readonly C4Element Postgres = new("postgres", "Ledger DB", C4Shape.Database, false) { Technology = "Postgres", Boundary = "ledger" };
    private static readonly C4Element Webhooks = new("webhooks", "Webhooks", C4Shape.Container, false) { Technology = "Go", Boundary = "ledger" };
    private static readonly C4Relation Charges = new("merchant", "payments", "Charges");
    private static readonly C4Relation Writes = new("payments", "postgres", "Writes ledger");
    private static readonly C4Relation Events = new("webhooks", "payments", "Verified events");

    public static FeatureStory Ledger()
    {
        var first = new C4Diagram(C4Level.Container, "Payments", [Merchant, Payments, Postgres], [new C4Boundary("ledger", "ledger-api")], [Charges, Writes]);
        var second = new C4Diagram(C4Level.Container, "Payments", [Merchant, Payments with { Description = "Checks a key first" }, Webhooks], [new C4Boundary("ledger", "ledger-api")], [Charges, Events]);
        var document = new VersionDocument("Payments on stripe-go v79", "What was proposed.", [], [new DocSection("payments", "Payments", "Owns every call to Stripe.")]);

        return new FeatureStory(
            new Feature("payments", "Payments on stripe-go v79"),
            [
                new PlayedVersion(1, VersionKind.Proposed, "v1-proposed.md", document, first,
                [
                    new PlayStep(StepKind.Base, "merchant", "Merchant", "Sells online.") { Elements = ["merchant"] },
                    new PlayStep(StepKind.Base, "payments", "Payments", "Owns every call to Stripe.") { Elements = ["payments"], Relations = [Charges] },
                    new PlayStep(StepKind.Base, "postgres", "Ledger DB", "Keeps the ledger.") { Elements = ["postgres"], Relations = [Writes] },
                    new PlayStep(StepKind.Flow, "a-charge", "Flow: a charge", "Two hops.") { Relations = [new C4Relation("merchant", "payments", "Asks for a charge"), new C4Relation("payments", "postgres", "Writes the ledger")] },
                ]),
                new PlayedVersion(2, VersionKind.Deviation, "v2-deviation.md", document with { Title = "Payments, v2" }, second,
                [
                    new PlayStep(StepKind.Added, "webhooks", "Webhooks", "Not in v1.") { Elements = ["webhooks"], Relations = [Events] },
                    new PlayStep(StepKind.Removed, "postgres", "Ledger DB", "Gone.") { Elements = ["postgres"] },
                    new PlayStep(StepKind.Changed, "payments", "Payments", "Checks a key first.") { Elements = ["payments"] },
                ])
                {
                    Removed = [Postgres],
                },
            ]);
    }
}
