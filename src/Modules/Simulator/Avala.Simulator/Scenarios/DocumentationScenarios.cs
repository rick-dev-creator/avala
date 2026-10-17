using Avala.Agents.Contracts.Events;
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

        ## Webhooks

        A new container receives Stripe's events and checks their signature before handing them on.

        ## Payments

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

    public static Scenario PlanRevised { get; } = new("plan-revised",
    [
        [
            new UpdatePlan([new PlanStep("Add a Webhooks container", PlanStepStatus.Done), new PlanStep("Apply the refund in Payments", PlanStepStatus.Done)]),
            new WriteFile(new ItemId("edit"), "WEBHOOKS.md", "# Webhooks\n"),
            Message("Added the webhooks."),
            .. Bill(2_400, 180, 0.0120m, 0.18),
            new Finish(),
        ],
        [
            new UpdatePlan([new PlanStep("Add a Webhooks container", PlanStepStatus.Done), new PlanStep("Keep idempotency keys in the ledger", PlanStepStatus.Done), new PlanStep("Apply the refund in Payments", PlanStepStatus.Done)]),
            new WriteFile(new ItemId("keys"), "WEBHOOKS.md", "# Webhooks\n\nIdempotent.\n"),
            Message("A refund Stripe sends twice ", "is now applied once."),
            .. Bill(2_100, 160, 0.0100m, 0.20),
            new Finish(),
        ],
    ]);

    public static IReadOnlyList<Scenario> All { get; } = [Document, PlanRevised];
}
