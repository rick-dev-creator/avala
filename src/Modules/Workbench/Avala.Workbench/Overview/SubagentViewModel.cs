using Avala.Agents.Contracts.Events;
using Avala.Delegation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Board;
using Avala.Workbench.Presenting;
using Avala.Workbench.Timeline;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Overview;

internal static class SubagentStates
{
    public static SubagentState Of(JobStatus status, Option<HoldReason> hold, bool asking) =>
        asking && status is JobStatus.Running or JobStatus.NeedsHelp ? SubagentState.Waiting : Of(status, hold);

    public static SubagentState Of(JobStatus status, Option<HoldReason> hold) => status switch
    {
        JobStatus.AwaitingReview or JobStatus.Approved => SubagentState.Completed,
        JobStatus.Failed => SubagentState.Failed,
        JobStatus.Discarded => SubagentState.Stopped,
        JobStatus.NeedsHelp when hold == Option<HoldReason>.Some(HoldReason.SessionLost) => SubagentState.Lost,
        JobStatus.NeedsHelp => SubagentState.Waiting,
        _ => SubagentState.Working,
    };

    public static string Text(SubagentState state) => state switch
    {
        SubagentState.Working => "working",
        SubagentState.Waiting => "waiting",
        SubagentState.Completed => "completed",
        SubagentState.Stopped => "stopped",
        SubagentState.Lost => "lost",
        _ => "failed",
    };
}

internal interface ISubagentViewModel
{
    string Name { get; }

    string Activity { get; }

    string Meter { get; }

    SubagentState State { get; }

    string StateText { get; }

    IReadOnlyList<string> Trail { get; }

    bool HasTrail { get; }

    bool IsOpen { get; }

    IRelayCommand ToggleCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class SubagentViewModel(ToolEntry entry, bool waiting) : ISubagentViewModel
{
    public const int Shown = 5;

    public string Name { get; } = entry.Named;

    public SubagentState State { get; } = entry.Outcome.Match(
        outcome => outcome switch
        {
            ItemOutcome.Succeeded => SubagentState.Completed,
            ItemOutcome.Cancelled => SubagentState.Stopped,
            ItemOutcome.Abandoned => SubagentState.Lost,
            _ => SubagentState.Failed,
        },
        () => waiting ? SubagentState.Waiting : SubagentState.Working);

    public string Activity { get; } = entry.Outcome.IsNone && entry.Doing.Length > 0 ? Shortened(entry.Doing) : Latest(entry.Output);

    public string Meter { get; } = Metered(entry);

    public IReadOnlyList<string> Trail { get; } = [.. entry.Trail.AsEnumerable().Reverse()];

    public bool HasTrail => Trail.Count > 0;

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [RelayCommand(CanExecute = nameof(HasTrail))]
    private void Toggle() => IsOpen = !IsOpen;

    public string StateText => SubagentStates.Text(State);

    public static IReadOnlyList<ISubagentViewModel> Of(Option<BoardJob> job) =>
        job.Match<IReadOnlyList<ISubagentViewModel>>(
            found =>
            {
                var asking = found.Transcript.Entries.OfType<PermissionEntry>().Where(permission => permission.AwaitsHuman).Select(permission => permission.Item).ToHashSet();

                return
                [
                    .. found.Transcript.Entries.OfType<ToolEntry>()
                        .Where(tool => tool.Kind == ItemKind.Subagent)
                        .TakeLast(Shown)
                        .Select(tool => new SubagentViewModel(tool, tool.Children.Overlaps(asking)))
                        .OrderBy(subagent => subagent.State is not (SubagentState.Working or SubagentState.Waiting)),
                ];
            },
            () => []);

    public static int Count(Option<BoardJob> job) =>
        job.Match(found => found.Transcript.Entries.OfType<ToolEntry>().Count(tool => tool.Kind == ItemKind.Subagent), () => 0);

    public static IReadOnlyList<ISubagentViewModel> Kept(IReadOnlyList<ISubagentViewModel> current, IReadOnlyList<ISubagentViewModel> next)
    {
        if (current.Select(Seen).SequenceEqual(next.Select(Seen)))
        {
            return current;
        }

        foreach (var agent in next.OfType<SubagentViewModel>())
        {
            agent.IsOpen = current.Any(shown => shown.Name == agent.Name && shown.IsOpen);
        }

        return next;
    }

    private static (string Name, SubagentState State, string Activity, string Meter, int Trail) Seen(ISubagentViewModel subagent) => (subagent.Name, subagent.State, subagent.Activity, subagent.Meter, subagent.Trail.Count);

    private static string Metered(ToolEntry entry) =>
        string.Join(
            " · ",
            new[]
            {
                entry.Steps > 0 ? OverviewPhrases.Count(entry.Steps, "tool use") : string.Empty,
                entry.Tokens > 0 ? Amounts.Tokens(entry.Tokens) : string.Empty,
            }.Where(part => part.Length > 0));

    private static string Latest(string output) =>
        Shortened(output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? string.Empty);

    private static string Shortened(string line)
    {
        var flat = line.ReplaceLineEndings(" ").Trim();

        return flat.Length <= 80 ? flat : $"{flat[..80]}…";
    }
}
