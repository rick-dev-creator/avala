using System.Globalization;
using System.Text.RegularExpressions;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Autopilot.Contracts;
using Avala.Budgets.Contracts;
using Avala.Delegation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Notifications.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;

namespace Avala.Notifications.Notices;

internal static partial class NoticeRules
{
    private static readonly HoldReason[] BudgetHolds = [HoldReason.BudgetExceeded, HoldReason.LimitNearlyReached, HoldReason.InvalidBudget];

    public static Option<Notice> Of(PermissionDecided decided) =>
        decided.Decision is { Delivery: DecisionDelivery.LeftToHuman } decision
            ? Decision(decision.Job, decision.Session, decision.Item, decision.At) with { Detail = $"{Words(decision.Kind)}: {decision.Target}" }
            : Option<Notice>.None;

    public static Option<Notice> Of(FormDecided decided) =>
        decided.Decision is { Delivery: DecisionDelivery.LeftToHuman, Answer.IsNone: true } decision
            ? Decision(decision.Job, decision.Session, decision.Item, decision.At) with { Detail = decision.Form.Title }
            : Option<Notice>.None;

    public static Notice Of(ParentAsked asked) =>
        Decision(asked.Delegation.Child, asked.Session, asked.Item, asked.Delegation.At) with { Detail = asked.Asking };

    public static Notice Of(JobHeld held, DateTimeOffset at) =>
        NeedsHelp(held.Hold.Job, BudgetHolds.Contains(held.Hold.Reason) ? NotificationKind.Budget : NotificationKind.Held, at) with { Detail = Words(held.Hold.Reason) };

    public static Notice Of(BudgetIntervened intervened)
    {
        var breach = intervened.Intervention.Breach;

        return NeedsHelp(intervened.Intervention.Hold.Job, NotificationKind.Budget, intervened.Intervention.At) with
        {
            Detail = string.Create(CultureInfo.InvariantCulture, $"{Words(breach.Measure)} {breach.Measured:0.##} of {breach.Cap:0.##}"),
        };
    }

    public static Option<Notice> Of(JobProgressed progressed, DateTimeOffset at) =>
        progressed.Status switch
        {
            JobStatus.NeedsHelp => NeedsHelp(progressed.Job, NotificationKind.Held, at),
            JobStatus.Failed => new Notice(NotificationKind.Failed, Subject(progressed.Job, "failed"), Subject(progressed.Job, "failed"), at) { Job = progressed.Job },
            JobStatus.AwaitingReview => new Notice(NotificationKind.Review, Subject(progressed.Job, "review"), Subject(progressed.Job, "review"), at) { Job = progressed.Job },
            _ => Option<Notice>.None,
        };

    public static Notice Of(LoopEnded ended, DateTimeOffset at) =>
        new(NotificationKind.LoopEnded, $"loop:{ended.State.Loop.Value:N}:ended", $"loop:{ended.State.Loop.Value:N}:ended", at)
        {
            Repository = JobFacts.Leaf(ended.State.Repository),
            Title = "Autopilot loop",
            Detail = ended.State.Ending.Map(Words),
        };

    public static IReadOnlyList<string> Resolved(PermissionAnswered answered) => [Item(answered.Answer.Session, answered.Answer.Item)];

    public static IReadOnlyList<string> Resolved(PermissionDecided decided) =>
        decided.Decision.Delivery is DecisionDelivery.Answered or DecisionDelivery.Withdrawn ? [Item(decided.Decision.Session, decided.Decision.Item)] : [];

    public static IReadOnlyList<string> Resolved(FormDecided decided) =>
        decided.Decision.Delivery is DecisionDelivery.Answered or DecisionDelivery.Withdrawn || decided.Decision.Answer.IsSome
            ? [Item(decided.Decision.Session, decided.Decision.Item)]
            : [];

    public static IReadOnlyList<string> Resolved(JobProgressed progressed) =>
    [
        .. progressed.Status == JobStatus.NeedsHelp ? Array.Empty<string>() : [Subject(progressed.Job, "needs-help")],
        .. progressed.Status == JobStatus.AwaitingReview ? Array.Empty<string>() : [Subject(progressed.Job, "review")],
    ];

    public static string Words<T>(T value)
        where T : struct, Enum
    {
        var spaced = Capitals().Replace(value.ToString(), " $1").Trim().ToLowerInvariant();

        return spaced.Length == 0 ? spaced : char.ToUpperInvariant(spaced[0]) + spaced[1..];
    }

    public static Notice OfParent(Notice notice, JobId parent) =>
        notice with { Subject = Subject(parent, "decision"), Job = parent };

    private static Notice Decision(Option<JobId> job, SessionId session, ItemId item, DateTimeOffset at) =>
        new(NotificationKind.Decision, job.Match(known => Subject(known, "decision"), () => $"session:{session.Value:N}:decision"), Item(session, item), at) { Job = job };

    private static Notice NeedsHelp(JobId job, NotificationKind kind, DateTimeOffset at) =>
        new(kind, Subject(job, "needs-help"), Subject(job, "needs-help"), at) { Job = job };

    private static string Subject(JobId job, string condition) => $"job:{job.Value:N}:{condition}";

    private static string Item(SessionId session, ItemId item) => $"item:{session.Value:N}:{item.Value}";

    [GeneratedRegex("(?<!^)([A-Z])", RegexOptions.CultureInvariant)]
    private static partial Regex Capitals();
}
