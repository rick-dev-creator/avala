using System.Globalization;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Budgets.Contracts;
using Avala.Delegation.Contracts;
using Avala.Handoffs.Contracts;
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
    public const string CouldNotStart = "could not start";

    public static string Chosen(ConnectionChoice choice) => choice.Reason switch
    {
        ChoiceReason.Person => $"Moved to {choice.Connection.Value} by you",
        ChoiceReason.AllAtLimit => $"Chosen by capacity: every connection was at its limit, so {choice.Connection.Value}, the least used",
        ChoiceReason.Preference => $"Chosen by preference: {choice.Connection.Value} was the first listed with room",
        ChoiceReason.SoonestReset => $"Chosen by reset: {choice.Connection.Value} had room and its window renews soonest",
        ChoiceReason.Rotation => $"Chosen in turn: {choice.Connection.Value} was next in the rotation",
        _ => $"Chosen by capacity: {choice.Connection.Value} had the most left",
    };

    public static string Capacity(CandidateCapacity candidate) =>
        Amounts.Joined([
            candidate.Window.Match(window => $"{Presenting.Amounts.Percent(candidate.Used)} of {window.Window}", () => "no usage reported"),
            .. candidate.Threshold < 1 ? [$"holds at {Presenting.Amounts.Percent(candidate.Threshold)}"] : Array.Empty<string>(),
            .. candidate.Available ? Array.Empty<string>() : ["at its limit"],
        ]);

    public static IReadOnlyList<HandoffLine> Handoffs(IReadOnlyList<HandoffRecord> handoffs, Option<UsageSummary> usage)
    {
        if (handoffs.Count == 0)
        {
            return [];
        }

        var total = usage.Match(found => (Costs: found.Costs, Tokens: found.Tokens.Input + found.Tokens.Output + found.Tokens.CacheRead + found.Tokens.CacheWrite + found.Tokens.Reasoning), () => (Costs: (IReadOnlyList<Cost>)[], Tokens: 0L));
        var before = handoffs.Aggregate(total, (left, handoff) => (Minus(left.Costs, handoff.Spent), left.Tokens - handoff.Tokens));

        return
        [
            .. handoffs.Select(handoff => new HandoffLine(
                Presenting.HandoffPhrases.Model(handoff).Match(
                    model => $"{Presenting.HandoffPhrases.Moves(handoff)} · {model} · {Presenting.Amounts.Time(handoff.At)}",
                    () => $"{Presenting.HandoffPhrases.Moves(handoff)} · {Presenting.Amounts.Time(handoff.At)}"),
                $"Spent on {handoff.From.Value}: {SpendLine(handoff.Spent, handoff.Tokens)}")),
            new HandoffLine(string.Empty, $"Spent on {handoffs[^1].To.Value}: {SpendLine(before.Costs, Math.Max(0, before.Tokens))}"),
        ];
    }

    public static string Waiting(ResetWait wait) => $"{char.ToUpperInvariant(Presenting.HandoffPhrases.Waits(wait)[0])}{Presenting.HandoffPhrases.Waits(wait)[1..]}";

    private static IReadOnlyList<Cost> Minus(IReadOnlyList<Cost> total, IReadOnlyList<Cost> before) =>
        [.. total.Select(cost => cost with { Amount = Math.Max(0, cost.Amount - before.Where(earlier => earlier.Currency == cost.Currency && earlier.Basis == cost.Basis).Sum(earlier => earlier.Amount)) })];

    private static string SpendLine(IReadOnlyList<Cost> costs, long tokens) =>
        $"{Presenting.Amounts.Costs(costs)} · {Presenting.Amounts.Tokens(tokens)}";

    public static IReadOnlyList<string> Attempts(IReadOnlyList<AttemptRecord> attempts, IReadOnlyList<VerificationReport> reports)
    {
        var verified = reports.GroupBy(report => report.Attempt).ToDictionary(group => group.Key, group => group.Last());

        return
        [
            .. attempts.Select((attempt, index) => verified.TryGetValue(attempt.Number, out var report)
                ? Attempt(report)
                : Unverified(attempt, index + 1 < attempts.Count ? attempts[index + 1].Origin : Option<AttemptOrigin>.None)),
            .. verified.Values.Where(report => attempts.All(attempt => attempt.Number != report.Attempt)).Select(Attempt),
        ];
    }

    private static string Unverified(AttemptRecord attempt, Option<AttemptOrigin> next) =>
        string.Create(CultureInfo.InvariantCulture, $"Attempt {attempt.Number}: ")
        + attempt.Outcome switch
        {
            AttemptOutcome.Running => "the agent is working, no checks yet",
            AttemptOutcome.AwaitingCheck => "checks running",
            AttemptOutcome.Interrupted when next == Option<AttemptOrigin>.Some(AttemptOrigin.Recovery) => "interrupted by a restart, no checks completed",
            AttemptOutcome.Interrupted => "interrupted, no checks ran",
            _ => "no checks ran",
        };

    public static string Attempt(VerificationReport report) =>
        string.Create(CultureInfo.InvariantCulture, $"Attempt {report.Attempt}: {Outcome(report.Outcome)}")
        + report.Problem.Match(problem => $" · {Problem(problem)}", () => string.Empty)
        + (report.Checks.Count == 0 ? string.Empty : $" · {string.Join(", ", report.Checks.Select(Check))}");

    public static Option<DecisionRow> Decision(PolicyDecision decision, IReadOnlyList<HumanAnswer> answers, Option<SessionId> latest) =>
        decision.Answer switch
        {
            PolicyAnswer.Allow => Option<DecisionRow>.Some(Row(DecisionVerdict.Allowed, decision.Kind, decision.Target, $"allowed {Ruled(decision.Rule)}")),
            PolicyAnswer.Deny => Option<DecisionRow>.Some(Row(DecisionVerdict.Denied, decision.Kind, decision.Target, $"denied {Ruled(decision.Rule)}")),
            _ when answers.Any(answer => answer.Session == decision.Session && answer.Item == decision.Item) => Option<DecisionRow>.None,
            _ => Option<DecisionRow>.Some(Asked(decision, latest)),
        };

    private static string Ruled(Option<PolicyRule> rule) => rule.Match(found => $"by rule {found.Name}", () => "by default");

    private static DecisionRow Asked(PolicyDecision decision, Option<SessionId> latest) =>
        decision.Delivery == DecisionDelivery.Withdrawn ? Row(DecisionVerdict.Unanswered, decision.Kind, decision.Target, "withdrawn by the harness")
        : decision.Delivery is not (DecisionDelivery.LeftToHuman or DecisionDelivery.LeftToParent) ? Row(DecisionVerdict.Unanswered, decision.Kind, decision.Target, decision.Parent.IsSome ? "asked its parent" : "asked you")
        : latest != Option<SessionId>.Some(decision.Session) ? Row(DecisionVerdict.Unanswered, decision.Kind, decision.Target, "unanswered, its session ended")
        : decision.Delivery == DecisionDelivery.LeftToParent ? Row(DecisionVerdict.Waiting, decision.Kind, decision.Target, "waiting for its parent")
        : Row(DecisionVerdict.Waiting, decision.Kind, decision.Target, "waiting for you");

    public static string ByParent(int answeredByParent, int givenToChildren) =>
        (answeredByParent > 0 ? $" · {Amounts.Count(answeredByParent, "answered by its parent", "answered by its parent")}" : string.Empty)
        + (givenToChildren > 0 ? $" · {Amounts.Count(givenToChildren, "answer given to its sub-agents", "answers given to its sub-agents")}" : string.Empty);

    public static DecisionRow Given(HumanAnswer answer) =>
        Row(Verdict(answer.Answer), answer.Kind, answer.Target, $"{Answered(answer.Answer)} for a sub-agent");

    public static DecisionRow Answer(HumanAnswer answer) =>
        Row(
            Verdict(answer.Answer),
            answer.Kind,
            answer.Target,
            $"{Answered(answer.Answer)} by {(answer.Parent.IsSome ? "its parent" : "you")}"
            + answer.Rule.Match(rule => $" · {rule.Name}", () => string.Empty)
            + (answer.Everything ? " · everything in this job" : string.Empty)
            + answer.RepositoryRule.Match(
                rule => $" · {rule.Name}",
                () => answer.RepositoryError.Match(_ => " · not added to the repository", () => string.Empty)));

    private static DecisionVerdict Verdict(PermissionAnswer answer) => answer == PermissionAnswer.Allow ? DecisionVerdict.Allowed : DecisionVerdict.Denied;

    private static string Answered(PermissionAnswer answer) => answer == PermissionAnswer.Allow ? "allowed" : "denied";

    private static DecisionRow Row(DecisionVerdict verdict, ItemKind kind, string target, string by) =>
        new(verdict, Presenting.CommandPhrases.Action(kind), Presenting.CommandPhrases.Target(kind, target), by);

    public static IReadOnlyList<DecisionRow> Grouped(IEnumerable<DecisionRow> rows) =>
        [.. rows.GroupBy(row => row).Select(group => group.Key with { Times = group.Count() }).OrderBy(row => row.Verdict)];

    public static AssumptionRow Assumption(Assumption assumption) =>
        new(assumption.Prompt, assumption.Chosen.Count > 0 ? string.Join(", ", assumption.Chosen) : "the agent's judgment");

    public static string Audit(int allowed, int byYou, int denied, int assumptions) =>
        allowed + byYou + denied + assumptions == 0
            ? "Nothing decided yet"
            : Amounts.Joined([
                .. Counted(allowed, "allowed by rules", "allowed by rules"),
                .. Counted(byYou, "answered by you", "answered by you"),
                .. Counted(denied, "denied", "denied"),
                .. Counted(assumptions, "assumption", "assumptions"),
            ]);

    private static string[] Counted(int count, string one, string many) => count > 0 ? [Amounts.Count(count, one, many)] : [];

    public static IReadOnlyList<string> Caps(BudgetCaps caps) =>
    [
        .. caps.CostPerJob.Select(cost => $"Cost cap {Amounts.Costs([cost])}"),
        .. caps.TokensPerJob.Match<string[]>(tokens => [$"Token cap {Amounts.Tokens(tokens)}"], () => []),
        .. caps.HoldAtLimit.Match<string[]>(threshold => [$"Holds at {Presenting.Amounts.Percent(threshold)} of a limit"], () => []),
        .. caps.MemoryPerJobMegabytes.Match<string[]>(megabytes => [string.Create(CultureInfo.InvariantCulture, $"Memory cap {megabytes} MB")], () => []),
        .. caps.CarvePerChild.Match<string[]>(share => [string.Create(CultureInfo.InvariantCulture, $"Each child gets {share * 100:0}% of what is left")], () => []),
    ];

    public static string Intervention(BudgetIntervention intervention, IReadOnlyList<Cost> spent) =>
        intervention.Breach.Measure is BudgetMeasure.Cost or BudgetMeasure.DailySpend
            ? $"Held for {Measure(intervention.Breach)}: {Presenting.Amounts.Measured(intervention.Breach.Measured, intervention.Breach.Subject, spent)} of {Amounts.Costs([new Cost(intervention.Breach.Cap, intervention.Breach.Subject)])}"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"Held for {Measure(intervention.Breach)}: {intervention.Breach.Measured} of {intervention.Breach.Cap} {intervention.Breach.Subject}");

    private static string Measure(BudgetBreach breach) =>
        breach.Measure == BudgetMeasure.DailySpend
            ? $"today's spend on {breach.Connection.Match(connection => connection.Value, () => "this machine")}"
            : breach.Measure.ToString();

    public static string Raise(BudgetRaise raise) =>
        $"Budget raised by {Raised(raise.By)}" + (raise.Child.IsSome ? " for a sub-agent" : string.Empty);

    public static string Continuing(BudgetRaise raise) => $"Your budget was raised by {Raised(raise.By)}. Carry on where you left off.";

    public static string RaiseRefused(BudgetError error) =>
        "The budget was not raised: " + (error == BudgetError.NotOverBudget ? "the job is no longer held over its budget" : "the budget refused it");

    public static string NotRetried(JobRejection rejection) => $"The job did not start again. {ConversationPhrases.Rejection(rejection)}";

    public static string NotContinued(JobRejection rejection) => $"The budget was raised, but the job did not continue. {ConversationPhrases.Rejection(rejection)}";

    public static bool Raisable(IReadOnlyList<BudgetIntervention> interventions) =>
        interventions is [.., { Hold.Reason: HoldReason.BudgetExceeded, Breach.Measure: BudgetMeasure.Cost or BudgetMeasure.Tokens }];

    private static string Raised(SpendCaps by) =>
        Amounts.Joined([.. Amounts.Priced(by.Cost), .. by.Tokens.Match<string[]>(tokens => [Amounts.Tokens(tokens)], () => [])]);

    public static string Carve(BudgetCarve carve) =>
        Amounts.Joined([
            carve.Contested
                ? string.Create(CultureInfo.InvariantCulture, $"A contest candidate: {carve.Share * 100:0}% of the per-job budget")
                : string.Create(CultureInfo.InvariantCulture, $"Carved {carve.Share * 100:0}% of its parent's budget"),
            .. Amounts.Priced(carve.Cost),
            .. carve.Tokens.Match<string[]>(tokens => [Amounts.Tokens(tokens)], () => []),
        ]);

    public static string Autonomy(SessionAutonomy autonomy) =>
        Level(autonomy) + (autonomy.ReadOnly ? " · read-only" : string.Empty);

    private static string Level(SessionAutonomy autonomy) =>
        autonomy.Raised.Match(at => $"Everything allowed in this job by you at {Presenting.Amounts.Time(at)}", () => Declared(autonomy));

    private static string Declared(SessionAutonomy autonomy) =>
        autonomy.Refused
            ? $"{autonomy.Effective}: asked for {autonomy.Requested.Match(requested => requested.ToString(), () => "more")}, refused by the repository"
            : autonomy.Effective == autonomy.Declared
                ? $"{autonomy.Effective}, as the repository declares"
                : $"{autonomy.Effective}, tightened from the repository's {autonomy.Declared}";

    public static string Checked(IReadOnlyList<VerificationReport> verifications) =>
        verifications.Count == 0
            ? "no checks yet"
            : verifications[^1] switch
            {
                { Outcome: VerificationOutcome.NoChecksDeclared } => "no checks declared",
                { Outcome: VerificationOutcome.Passed or VerificationOutcome.Failed } last => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{last.Checks.Count(check => check.Status == CheckStatus.Passed)} of {last.Checks.Count} {(last.Checks.Count == 1 ? "check" : "checks")} passed"),
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
                limit => $"{Presenting.Amounts.Measured(Spent(spent, limit.Currency).Amount, limit.Currency, spent.Costs)} of {Amounts.Costs([limit])}",
                () => spent.Costs.Count > 0 ? Amounts.Costs(spent.Costs) : Amounts.Tokens(Amounts.Total(spent.Tokens))),
            () => cap.Match(limit => $"capped at {Amounts.Costs([limit])}", () => "nothing yet"));

    public static Option<double> Share(Option<UsageSummary> usage, Option<Cost> cap) =>
        cap.Bind(limit => limit.Amount <= 0
            ? Option<double>.None
            : Option<double>.Some((double)(usage.Match(spent => Spent(spent, limit.Currency).Amount, () => 0m) / limit.Amount)));

    public static string Scratch(string fact) => $"{fact} · scratch";

    public static string FolderUnopened(string folder) => $"The folder could not be opened here: it is {folder}.";

    public static string Worktree(string branch, Option<string> baseBranch)
    {
        var name = branch.Split('/')[^1];
        var label = name.Length > 12 && name.All(char.IsAsciiHexDigit) ? name[..7] : name;

        return baseBranch.Match(from => $"{label} from {from}", () => label);
    }

    private static Cost Spent(UsageSummary usage, string currency) =>
        new(usage.Costs.Where(cost => cost.Currency == currency).Sum(cost => cost.Amount), currency);

    private static string Outcome(VerificationOutcome outcome) => outcome switch
    {
        VerificationOutcome.Passed => "passed",
        VerificationOutcome.Failed => "failed",
        VerificationOutcome.NoChecksDeclared => "no checks declared",
        _ => "invalid declaration",
    };

    private static string Problem(DeclarationProblem problem) => problem switch
    {
        DeclarationProblem.MissingCommand => "a check has no command",
        DeclarationProblem.InvalidTimeout => "a check has a timeout that is not a number of seconds between 0 and 86400",
        DeclarationProblem.Unreadable => "it could not be read from the job's commit",
        _ => "it is not a JSON object with a checks array of check objects",
    };

    private static string Status(CheckStatus status) => status switch
    {
        CheckStatus.Passed => "passed",
        CheckStatus.Failed => "failed",
        CheckStatus.TimedOut => "timed out",
        CheckStatus.NotFound => "not found",
        _ => "skipped",
    };

    private static string Check(CheckEvidence check) =>
        $"{check.Name} {Status(check.Status)}"
        + (check.Status == CheckStatus.Skipped
            ? string.Empty
            : $" ({check.ExitCode.Match(code => string.Create(CultureInfo.InvariantCulture, $"exit {code}, "), () => string.Empty)}{Presenting.Amounts.Seconds(check.Duration)} s)");
}
