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

    public const string Decision = """
        # Keep idempotency keys in the ledger

        ## Status

        Accepted

        ## Context

        Stripe can send the same refund event more than once.

        ## Decision

        The ledger keeps each event's key and applies a refund only for a key it has not seen.

        ## Alternatives

        A separate deduplication service, ruled out as one more thing to run.

        ## Consequences

        The ledger's schema gains a keys table.

        """;

    public static Scenario DocumentDecision { get; } = new("document-decision",
    [
        [
            Thought("The plan changed where refunds are deduplicated, ", "which is hard to reverse."),
            new WriteFile(new ItemId("version"), DocumentationDraft.Path, Version),
            new WriteFile(new ItemId("decision"), DocumentationDraft.Decision, Decision),
            Message("Wrote the version ", "and its decision record."),
            .. Bill(1_300, 520, 0.0024m, 0.09),
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

    public static Scenario PlanWhenAsked { get; } = new("plan-when-asked",
    [
        [
            new IfTold("task list", Told: true, new UpdatePlan([new PlanStep("Write the changelog", PlanStepStatus.Done), new PlanStep("Link it from the README", PlanStepStatus.Done)])),
            new WriteFile(new ItemId("edit"), "CHANGELOG.md", "# Changelog\n"),
            new Recall(new ItemId("told")),
            .. Bill(1_800, 150, 0.0090m, 0.12),
            new Finish(),
        ],
        [
            new Recall(new ItemId("told")),
            .. Bill(1_200, 90, 0.0060m, 0.13),
            new Finish(),
        ],
    ]);

    public static IReadOnlyList<Scenario> All { get; } = [Document, DocumentDecision, PlanRevised, PlanWhenAsked];
}
