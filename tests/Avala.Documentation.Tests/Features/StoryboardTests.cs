using Avala.Documentation.Contracts;
using Avala.Documentation.Features;
using Avala.Sdk;

namespace Avala.Documentation.Tests.Features;

public sealed class StoryboardTests
{
    private const string Proposed = """
        # Payments on stripe-go v79

        What the orchestrator proposed.

        ```mermaid
        C4Container
          Person(merchant, "Merchant")
          Container(payments, "Payments", "Go")
          ContainerDb(postgres, "Ledger DB", "Postgres")
          Rel(merchant, payments, "Charges")
          Rel(payments, postgres, "Writes ledger")
        ```

        ```mermaid
        C4Dynamic
          title A charge
          RelIndex(2, payments, postgres, "The entry is written")
          RelIndex(1, merchant, payments, "The merchant asks")
        ```

        ## Payments {#payments}

        A new container owns every call to Stripe.

        ## Merchant {#merchant}

        Nothing changes for them.

        ## Flow: a charge {#a-charge}

        Two hops.
        """;

    private const string Deviation = """
        # Payments on stripe-go v79

        Webhooks and idempotency.

        ```mermaid
        C4Container
          Person(merchant, "Merchant")
          Container(payments, "Payments", "Go", "Checks a key first")
          Container(webhooks, "Webhooks", "Go")
          Rel(merchant, payments, "Charges")
          Rel(webhooks, payments, "Verified events")
        ```

        ## Webhooks {#webhooks}

        Not in v1.

        ## Ledger DB, removed {#postgres}

        Gone.
        """;

    [Fact]
    public void ADocumentIsReadIntoItsTitleLeadDiagramsAndAnchoredSections()
    {
        var document = VersionReader.Read(Proposed + "\n```mermaid\nflowchart LR\n  a --> b\n```\n");

        Assert.Equal(("Payments on stripe-go v79", "What the orchestrator proposed."), (document.Title, document.Lead));
        Assert.Equal([C4Level.Container, C4Level.Dynamic], document.Diagrams.Select(diagram => diagram.Level));
        Assert.Equal([("payments", "Payments", "A new container owns every call to Stripe."), ("merchant", "Merchant", "Nothing changes for them."), ("a-charge", "Flow: a charge", "Two hops.")], document.Sections.Select(section => (section.Anchor, section.Title, section.Text)));
        Assert.Equal(["flowchart LR\n  a --> b\n"], document.Unread);
    }

    [Fact]
    public void TheFirstVersionBuildsUpElementByElementInTheOrderOfItsSectionsThenPlaysItsFlow()
    {
        var played = Storyboard.Play(1, VersionKind.Proposed, "v1-proposed.md", VersionReader.Read(Proposed), Option<C4Diagram>.None);

        Assert.Equal(
            [(StepKind.Base, "payments", "payments"), (StepKind.Base, "merchant", "merchant"), (StepKind.Flow, "a-charge", ""), (StepKind.Base, "postgres", "postgres")],
            played.Steps.Select(step => (step.Kind, step.Anchor, string.Join(',', step.Elements))));
        Assert.Equal(["merchant>payments"], played.Steps[1].Relations.Select(relation => $"{relation.From}>{relation.To}"));
        Assert.Equal(["merchant>payments", "payments>postgres"], played.Steps[2].Relations.Select(relation => $"{relation.From}>{relation.To}"));
        Assert.Equal(["payments>postgres"], played.Steps[3].Relations.Select(relation => $"{relation.From}>{relation.To}"));
    }

    [Fact]
    public void ALaterVersionShowsWhatIsNewChangedAndRemovedAgainstTheOneBefore()
    {
        var before = Storyboard.Structure(VersionReader.Read(Proposed));

        var played = Storyboard.Play(2, VersionKind.Deviation, "v2-deviation.md", VersionReader.Read(Deviation), before);

        Assert.Equal(
            [(StepKind.Added, "webhooks", "Webhooks"), (StepKind.Removed, "postgres", "Ledger DB, removed"), (StepKind.Changed, "payments", "Payments")],
            played.Steps.Select(step => (step.Kind, step.Anchor, step.Title)));
        Assert.Equal(["webhooks>payments"], played.Steps[0].Relations.Select(relation => $"{relation.From}>{relation.To}"));
        Assert.Equal(["postgres"], played.Removed.Select(element => element.Alias));
    }
}
