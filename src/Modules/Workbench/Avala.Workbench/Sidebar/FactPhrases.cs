using System.Globalization;
using Avala.Agents.Contracts.Events;
using Avala.Components.Status;
using Avala.Jobs.Contracts;
using Avala.Workbench.Board;

namespace Avala.Workbench.Sidebar;

internal static class FactPhrases
{
    private const int TitleLength = 80;

    public static string Title(string instruction)
    {
        var line = instruction.Trim().Split('\n', 2)[0].Trim();

        return line.Length <= TitleLength ? line : $"{line[..(TitleLength - 1)].TrimEnd()}…";
    }

    public static string Of(JobFact fact) => fact.Kind switch
    {
        FactKind.Working => "working",
        FactKind.PlanProgress => string.Create(CultureInfo.InvariantCulture, $"{fact.Done} of {fact.Total}"),
        FactKind.Verifying => "verifying",
        FactKind.AsksPermission => fact.Item.Match(Permission, () => "asks permission"),
        FactKind.AsksQuestion => "asks a question",
        FactKind.AsksPlanApproval => "asks to approve a plan",
        FactKind.AsksForInput => "asks for input",
        FactKind.Held => fact.Hold.Match(Held, () => "held"),
        FactKind.NeedsHelp => "needs help: retries ran out",
        FactKind.Verified => fact.Attempt > 1
            ? string.Create(CultureInfo.InvariantCulture, $"verified on attempt {fact.Attempt}")
            : "verified",
        FactKind.NoChecks => "no checks declared",
        FactKind.ReadyForReview => "ready for review",
        FactKind.Approved => fact.Strategy.Match(strategy => strategy == "merge" ? "merged" : "approved", () => "approved"),
        FactKind.Discarded => "discarded",
        FactKind.Failed => "failed",
        _ => "starting",
    };

    public static StatusKind Dot(BoardJob job) => job.Group switch
    {
        JobGroup.NeedsYou => job.PendingDecisions == 0 && job.Hold.IsSome ? StatusKind.Held : StatusKind.NeedsYou,
        JobGroup.ReadyForReview => StatusKind.ReadyForReview,
        JobGroup.Done => job.Status == JobStatus.Failed ? StatusKind.Failed : StatusKind.Done,
        _ => job.Status == JobStatus.Checking ? StatusKind.Checking : StatusKind.Working,
    };

    public static string Asking(ItemKind kind) => Permission(kind);

    public static string Asking(FormPurpose purpose) => purpose switch
    {
        FormPurpose.Question => "asks a question",
        FormPurpose.PlanApproval => "asks to approve a plan",
        FormPurpose.Permission => "asks permission",
        _ => "asks for input",
    };

    private static string Permission(ItemKind kind) => kind switch
    {
        ItemKind.Command => "wants to run a command",
        ItemKind.FileEdit => "wants to edit a file",
        ItemKind.Web => "wants to reach the web",
        ItemKind.Mcp => "wants to use a tool",
        _ => "asks permission",
    };

    private static string Held(HoldReason reason) => reason switch
    {
        HoldReason.Stalled => "held: stalled",
        HoldReason.SessionLost => "held: session lost",
        HoldReason.BudgetExceeded => "held: over budget",
        HoldReason.LimitNearlyReached => "held: near its usage limit",
        HoldReason.InvalidBudget => "held: invalid budget",
        HoldReason.MemoryExceeded => "held: out of memory",
        HoldReason.Stopped => "stopped",
        _ => "interrupted",
    };
}
