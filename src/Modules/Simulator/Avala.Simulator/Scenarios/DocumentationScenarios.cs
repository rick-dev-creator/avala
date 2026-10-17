using Avala.Agents.Contracts.Sessions;
using Avala.Documentation.Contracts;
using static Avala.Simulator.Scenarios.ScenarioCatalog;

namespace Avala.Simulator.Scenarios;

internal static class DocumentationScenarios
{
    public const string Version = """
        # Refund webhooks

        Stripe's refund events reach the ledger through a container that checks their signature.

        ```mermaid
        C4Container
          title Refund webhooks
          System_Ext(stripe, "Stripe", "API 2026-09")
          System_Boundary(ledger, "ledger-api") {
            Container(webhooks, "Webhooks", "Go", "Verifies Stripe's signature")
            Container(payments, "Payments", "Go", "Applies refunds")
          }
          Rel(stripe, webhooks, "Refund events")
          Rel(webhooks, payments, "Verified events")
        ```

        ## Webhooks {#webhooks}

        A new container receives Stripe's events and checks their signature before handing them on.

        ## Payments {#payments}

        Payments applies each verified refund to the ledger.

        """;

    public static Scenario Document { get; } = new("document",
    [
        [
            Thought("The plan names two containers ", "and the relation between them."),
            new WriteFile(new ItemId("version"), DocumentationDraft.Path, Version),
            Message("Wrote the version ", "with its C4."),
            .. Bill(1_100, 420, 0.0020m, 0.08),
            new Finish(),
        ],
    ]);

    public static IReadOnlyList<Scenario> All { get; } = [Document];
}
