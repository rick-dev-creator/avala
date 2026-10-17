using Avala.Documentation.Contracts;
using Avala.Documentation.Features;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Documentation.Tests.Features;

public sealed class MermaidC4Tests
{
    [Fact]
    public void AContainerDiagramIsReadWithItsElementsBoundariesAndRelations()
    {
        var diagram = Outcomes.Present(MermaidC4.Read("""
            C4Container
              title Payments on stripe-go v79
              %% a comment
              Person(merchant, "Merchant", "Sells online")
              System_Ext(stripe, "Stripe", "API 2026-09")
              System_Boundary(ledger, "ledger-api") {
                Container(payments, "Payments", "Go, stripe-go v79", "Owns every call to Stripe")
                ContainerDb(postgres, "Ledger DB", "Postgres")
              }
              Rel(merchant, payments, "Charges, refunds", "HTTPS")
              Rel_Back(stripe, payments, "Calls")
              UpdateLayoutConfig($c4ShapeInRow="3")
            """));

        Assert.Equal((C4Level.Container, "Payments on stripe-go v79"), (diagram.Level, diagram.Title));
        Assert.Equal(
            [
                new C4Element("merchant", "Merchant", C4Shape.Person, false) { Description = "Sells online" },
                new C4Element("stripe", "Stripe", C4Shape.System, true) { Description = "API 2026-09" },
                new C4Element("payments", "Payments", C4Shape.Container, false) { Technology = "Go, stripe-go v79", Description = "Owns every call to Stripe", Boundary = "ledger" },
                new C4Element("postgres", "Ledger DB", C4Shape.Database, false) { Technology = "Postgres", Boundary = "ledger" },
            ],
            diagram.Elements);
        Assert.Equal([new C4Boundary("ledger", "ledger-api")], diagram.Boundaries);
        Assert.Equal([new C4Relation("merchant", "payments", "Charges, refunds") { Technology = "HTTPS" }, new C4Relation("payments", "stripe", "Calls")], diagram.Relations);
    }

    [Fact]
    public void ADynamicDiagramsRelationsAreNumberedInOrderOrByTheirIndex()
    {
        var indexed = Outcomes.Present(MermaidC4.Read("C4Dynamic\n  title A charge\n  RelIndex(2, api, payments, \"Hands it on\")\n  RelIndex(1, merchant, api, \"Asks for a charge\")"));
        var ordered = Outcomes.Present(MermaidC4.Read("C4Dynamic\n  Rel(merchant, api, \"Asks\")\n  Rel(api, payments, \"Hands on\")"));

        Assert.Equal([Option<int>.Some(2), Option<int>.Some(1)], indexed.Relations.Select(relation => relation.Index));
        Assert.Equal([Option<int>.Some(1), Option<int>.Some(2)], ordered.Relations.Select(relation => relation.Index));
    }

    [Theory]
    [InlineData("flowchart LR\n  a --> b")]
    [InlineData("")]
    public void MermaidThatIsNotAC4DiagramIsNotRead(string source) =>
        Assert.True(MermaidC4.Read(source).IsNone);
}
