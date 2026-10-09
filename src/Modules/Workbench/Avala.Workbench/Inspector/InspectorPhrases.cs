using System.Globalization;
using Avala.Agents.Contracts.Events;
using Avala.Budgets.Contracts;
using Avala.Delegation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Verification.Contracts;
using Avala.Workbench.Conversation;
using Avala.Workbench.Review;
using Avala.Workbench.Sidebar;

namespace Avala.Workbench.Inspector;

internal static class InspectorPhrases
{
    public static string Chosen(ConnectionChoice choice) =>
        choice.Reason == ChoiceReason.AllAtLimit
            ? $"Chosen by capacity: every connection was at its limit, so {choice.Connection.Value}, the least used"
            : $"Chosen by capacity: {choice.Connection.Value} had the most left";

    public static string Capacity(CandidateCapacity candidate) =>
        Amounts.Joined([
            candidate.Window.Match(window => $"{Presenting.Amounts.Percent(candidate.Used)} of {window.Window}", () => "no usage reported"),
            .. candidate.Threshold < 1 ? [$"holds at {Presenting.Amounts.Percent(candidate.Threshold)}"] : Array.Empty<string>(),
            .. candidate.Available ? Array.Empty<string>() : ["at its limit"],
        ]);

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

    public static string Checked(IReadOnlyList<VerificationReport> verifications) =>
        verifications.Count == 0
            ? "no checks yet"
            : verifications[^1] switch
            {
                { Outcome: VerificationOutcome.NoChecksDeclared } => "no checks declared",
                { Outcome: VerificationOutcome.Passed or VerificationOutcome.Failed } last => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{last.Checks.Count(check => check.Status == CheckStatus.Passed)} of {last.Checks.Count} passed"),
                _ => "invalid declaration",
            };

    public static string Decided(int allowed, int byYou, int denied, int assumptions) =>
        denied > 0 ? Amounts.Count(denied, "denied", "denied")
        : assumptions > 0 ? Amounts.Count(assumptions, "assumption", "assumptions")
        : allowed > 0 ? Amounts.Count(allowed, "allowed by rules", "allowed by rules")
        : byYou > 0 ? Amounts.Count(byYou, "answered by you", "answered by you")
        : "nothing yet";

    public static string Spending(Option<UsageSummary> usage, Option<Cost> cap) =>
        usage.Match(
            spent => cap.Match(
                limit => $"{Amounts.Costs([Spent(spent, limit.Currency)])} of {Amounts.Costs([limit])}",
                () => spent.Costs.Count > 0 ? Amounts.Costs(spent.Costs) : Amounts.Tokens(Amounts.Total(spent.Tokens))),
            () => cap.Match(limit => $"capped at {Amounts.Costs([limit])}", () => "nothing yet"));

    public static Option<double> Share(Option<UsageSummary> usage, Option<Cost> cap) =>
        cap.Bind(limit => limit.Amount <= 0
            ? Option<double>.None
            : Option<double>.Some((double)(usage.Match(spent => Spent(spent, limit.Currency).Amount, () => 0m) / limit.Amount)));

    public static string Delegated(int children, bool delegatedByParent) =>
        children > 0 ? Amounts.Count(children, "sub-agent", "sub-agents")
        : delegatedByParent ? "a sub-agent"
        : "none";

    public static string Branch(string branch) => branch.Split('/')[^1];

    private static Cost Spent(UsageSummary usage, string currency) =>
        new(usage.Costs.Where(cost => cost.Currency == currency).Sum(cost => cost.Amount), currency);

    private static string Outcome(VerificationOutcome outcome) => outcome switch
    {
        VerificationOutcome.Passed => "passed",
        VerificationOutcome.Failed => "failed",
        VerificationOutcome.NoChecksDeclared => "no checks declared",
        _ => "invalid declaration",
    };

    private static string Check(CheckEvidence check) =>
        $"{check.Name} {check.Status.ToString().ToLowerInvariant()}"
        + (check.Status == CheckStatus.Skipped
            ? string.Empty
            : $" ({check.ExitCode.Match(code => string.Create(CultureInfo.InvariantCulture, $"exit {code}, "), () => string.Empty)}{Presenting.Amounts.Seconds(check.Duration)} s)");
}
