using System.Collections.Frozen;
using System.Globalization;
using Avala.Agents.Contracts.Events;
using Avala.Components.Status;
using Avala.Jobs.Contracts;
using Avala.Workbench.Board;
using Avala.Workbench.Presenting;

namespace Avala.Workbench.Sidebar;

internal static class FactPhrases
{
    private const int TitleLength = 80;

    private static readonly FrozenDictionary<FactKind, string> Plain = new Dictionary<FactKind, string>
    {
        [FactKind.Working] = "working",
        [FactKind.WaitsForParent] = "waiting for its parent",
        [FactKind.Verifying] = "verifying",
        [FactKind.AsksQuestion] = "asks a question",
        [FactKind.AsksPlanApproval] = "asks to approve a plan",
        [FactKind.AsksForInput] = "asks for input",
        [FactKind.NeedsHelp] = "needs help: retries ran out",
        [FactKind.NoChecks] = "no checks declared",
        [FactKind.ReadyForReview] = "ready for review",
        [FactKind.Discarded] = "discarded",
        [FactKind.Failed] = "failed",
    }.ToFrozenDictionary();

    public static string Title(string instruction)
    {
        var line = instruction.Trim().Split('\n', 2)[0].Trim();

        return line.Length <= TitleLength ? line : $"{line[..(TitleLength - 1)].TrimEnd()}…";
    }

    public static string Of(JobFact fact) => fact.Kind switch
    {
        FactKind.PlanProgress => string.Create(CultureInfo.InvariantCulture, $"{fact.Done} of {fact.Total}"),
        FactKind.AsksPermission => fact.Item.Match(Permission, () => "asks permission"),
        FactKind.Held => fact.Hold.Match(Held, () => "held"),
        FactKind.ResumesAtReset => fact.Wait.Match(HandoffPhrases.Resumes, () => "held"),
        FactKind.Verified => fact.Attempt > 1
            ? string.Create(CultureInfo.InvariantCulture, $"verified on attempt {fact.Attempt}")
            : "verified",
        FactKind.Delivered => fact.Total > 0
            ? string.Create(CultureInfo.InvariantCulture, $"delivered · #{fact.PullRequest} · checks {fact.Done} of {fact.Total}")
            : string.Create(CultureInfo.InvariantCulture, $"delivered · #{fact.PullRequest}"),
        FactKind.PullRequestMerged => string.Create(CultureInfo.InvariantCulture, $"merged · #{fact.PullRequest}"),
        FactKind.PullRequestClosed => string.Create(CultureInfo.InvariantCulture, $"closed · #{fact.PullRequest}"),
        FactKind.Approved => fact.Strategy.Match(strategy => strategy == "merge" ? "merged" : "approved", () => "approved"),
        _ => Plain.GetValueOrDefault(fact.Kind, "starting"),
    };

    public static StatusKind Dot(BoardJob job) => job.Group switch
    {
        JobGroup.NeedsYou => job.PendingDecisions == 0 && job.Hold.IsSome ? StatusKind.Held : StatusKind.NeedsYou,
        JobGroup.Waiting => StatusKind.Held,
        JobGroup.ReadyForReview => StatusKind.ReadyForReview,
        JobGroup.Delivered => StatusKind.Working,
        JobGroup.Done => job.Status == JobStatus.Failed ? StatusKind.Failed : StatusKind.Done,
        _ when job.Fact.Kind == FactKind.WaitsForParent => StatusKind.Held,
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
        HoldReason.InvalidChecks => "held: invalid checks",
        HoldReason.MemoryExceeded => "held: out of memory",
        HoldReason.Stopped => "stopped",
        HoldReason.NotResumable => "held: its conversation cannot resume",
        HoldReason.CouldNotStart => "could not start",
        _ => "interrupted",
    };
}
