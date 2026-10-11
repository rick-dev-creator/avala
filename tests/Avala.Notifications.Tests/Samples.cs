using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Autopilot.Contracts;
using Avala.Budgets.Contracts;
using Avala.Jobs.Contracts;
using Avala.Notifications.Contracts;
using Avala.Notifications.Notices;
using Avala.Permissions.Contracts;
using Avala.Sdk;

namespace Avala.Notifications.Tests;

internal static class Samples
{
    public const string Repository = "/work/ledger-api";

    public const string Instruction = "Fix the flaky login test\n\nIt fails one run in ten.";

    public static readonly DateTimeOffset Monday = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);

    public static readonly JobId Job = new(Guid.Parse("0b5c1d2e-0000-7000-8000-000000000001"));

    public static readonly JobId Other = new(Guid.Parse("0b5c1d2e-0000-7000-8000-000000000002"));

    public static readonly SessionId Session = new(Guid.Parse("0b5c1d2e-0000-7000-8000-0000000000aa"));

    public static PermissionDecided Asked(string item, DecisionDelivery delivery = DecisionDelivery.LeftToHuman) =>
        new(new PolicyDecision(Session, TurnId.New(), new ItemId(item), Job, ItemKind.Command, "dotnet test", PolicyAnswer.Ask, Option<PolicyRule>.None, delivery, Monday));

    public static PermissionAnswered Answered(string item) =>
        new(new HumanAnswer(Session, Job, new ItemId(item), ItemKind.Command, "dotnet test", PermissionAnswer.Allow, Option<string>.None, Option<PolicyRule>.None, Monday));

    public static FormDecided Form(string item, DecisionDelivery delivery, Option<FormAnswer> answer) =>
        new(new FormDecision(Session, TurnId.New(), new ItemId(item), Job, new AgentForm(FormPurpose.Question, "Which database?", "context", []), Autonomy.Supervised, answer, [], delivery, Monday));

    public static JobHeld Held(HoldReason reason) => new(new JobHold(Job, Session, reason, SessionHalt.Interrupted));

    public static BudgetIntervened OverBudget() =>
        new(new BudgetIntervention(new JobHold(Job, Session, HoldReason.BudgetExceeded, SessionHalt.Interrupted), new BudgetBreach(BudgetMeasure.Cost, "job", 5.2m, 5m, Option<BudgetError>.None), Monday));

    public static LoopEnded Ended() =>
        new(new LoopState(new LoopId(Guid.Parse("0b5c1d2e-0000-7000-8000-0000000000bb")), Repository, LoopStatus.Ended, Monday, 3, Option<JobId>.None) { Ending = LoopEnding.Drained });

    public static Notice Decision(JobId job, string repository = "ledger-api") =>
        new(NotificationKind.Decision, $"job:{job.Value:N}:decision", $"item:{job.Value:N}", Monday)
        {
            Job = job,
            Repository = repository,
            Title = "Fix the flaky login test",
            Detail = "Command: dotnet test",
        };

    public static Notice Failed(JobId job) =>
        new(NotificationKind.Failed, $"job:{job.Value:N}:failed", $"job:{job.Value:N}:failed", Monday) { Job = job, Repository = "ledger-api", Title = "Fix the flaky login test" };
}
