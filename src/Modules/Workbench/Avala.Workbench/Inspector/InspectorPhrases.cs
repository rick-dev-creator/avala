using System.Globalization;
using Avala.Agents.Contracts.Events;
using Avala.Budgets.Contracts;
using Avala.Delegation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Verification.Contracts;
using Avala.Workbench.Conversation;
using Avala.Workbench.Review;
using Avala.Workbench.Sidebar;

namespace Avala.Workbench.Inspector;

internal static class InspectorPhrases
{
    public static string Attempt(VerificationReport report) =>
        string.Create(CultureInfo.InvariantCulture, $"Attempt {report.Attempt}: {Outcome(report.Outcome)}")
        + (report.Checks.Count == 0 ? string.Empty : $" · {string.Join(", ", report.Checks.Select(Check))}");

    public static string Decision(PolicyDecision decision) =>
        $"{(decision.Answer == PolicyAnswer.Allow ? "Allowed" : decision.Answer == PolicyAnswer.Deny ? "Denied" : "Asked you")} {decision.Kind} {decision.Target}"
        + decision.Rule.Match(rule => $" · rule {rule.Name}", () => decision.Delivery == DecisionDelivery.Answered ? " · default" : string.Empty);

    public static string Answer(HumanAnswer answer) =>
        $"You {(answer.Answer == PermissionAnswer.Allow ? "allowed" : "denied")} {answer.Kind} {answer.Target}"
        + answer.SessionRule.Match(_ => " · don't ask again", () => string.Empty);

    public static string Assumption(Assumption assumption) =>
        $"{assumption.Prompt}: {(assumption.Chosen.Count > 0 ? string.Join(", ", assumption.Chosen) : "the agent's judgment")}";

    public static string Audit(int allowed, int byYou, int denied, int assumptions) =>
        Amounts.Joined([
            Amounts.Count(allowed, "allowed by rules", "allowed by rules"),
            Amounts.Count(byYou, "answered by you", "answered by you"),
            Amounts.Count(denied, "denied", "denied"),
            Amounts.Count(assumptions, "assumption", "assumptions"),
        ]);

    public static IReadOnlyList<string> Caps(BudgetCaps caps) =>
    [
        .. caps.CostPerJob.Select(cost => $"Cost cap {Amounts.Costs([cost])}"),
        .. caps.TokensPerJob.Match<string[]>(tokens => [$"Token cap {Amounts.Tokens(tokens)}"], () => []),
        .. caps.HoldAtLimit.Match<string[]>(threshold => [string.Create(CultureInfo.InvariantCulture, $"Held at {threshold * 100:0}% of a usage limit")], () => []),
        .. caps.MemoryPerJobMegabytes.Match<string[]>(megabytes => [string.Create(CultureInfo.InvariantCulture, $"Memory cap {megabytes} MB")], () => []),
        .. caps.CarvePerChild.Match<string[]>(share => [string.Create(CultureInfo.InvariantCulture, $"Each child gets {share * 100:0}% of what is left")], () => []),
    ];

    public static string Intervention(BudgetIntervention intervention) =>
        string.Create(CultureInfo.InvariantCulture, $"Held for {intervention.Breach.Measure}: {intervention.Breach.Measured} of {intervention.Breach.Cap} {intervention.Breach.Subject}");

    public static string Carve(BudgetCarve carve) =>
        Amounts.Joined([
            string.Create(CultureInfo.InvariantCulture, $"Carved {carve.Share * 100:0}% of its parent's budget"),
            .. Amounts.Priced(carve.Cost),
            .. carve.Tokens.Match<string[]>(tokens => [Amounts.Tokens(tokens)], () => []),
        ]);

    public static string Autonomy(SessionAutonomy autonomy) =>
        autonomy.Refused
            ? $"{autonomy.Effective}: asked for {autonomy.Requested.Match(requested => requested.ToString(), () => "more")}, refused by the repository"
            : autonomy.Effective == autonomy.Declared
                ? $"{autonomy.Effective}, as the repository declares"
                : $"{autonomy.Effective}, tightened from the repository's {autonomy.Declared}";

    public static string Child(JobSummary child, Option<DelegationRecord> delegation) =>
        Amounts.Joined([
            FactPhrases.Title(child.Instruction),
            child.Status.ToString(),
            .. child.Connection.Match<string[]>(connection => [connection.Value], () => []),
            .. delegation.Bind(record => record.Report).Match<string[]>(report => [report.Outcome.ToString()], () => []),
        ]);

    public static string Refused(DelegationRecord record) =>
        $"Refused: {FactPhrases.Title(record.Instruction)}"
        + record.Refusal.Match(refusal => $" · {refusal}", () => record.Rejection.Match(rejection => $" · {ConversationPhrases.Rejection(rejection)}", () => string.Empty));

    private static string Outcome(VerificationOutcome outcome) => outcome switch
    {
        VerificationOutcome.Passed => "passed",
        VerificationOutcome.Failed => "failed",
        VerificationOutcome.NoChecksDeclared => "no checks declared",
        _ => "invalid declaration",
    };

    private static string Check(CheckEvidence check) =>
        $"{check.Name} {check.Status.ToString().ToLowerInvariant()}"
        + check.ExitCode.Match(code => string.Create(CultureInfo.InvariantCulture, $" (exit {code})"), () => string.Empty);
}
