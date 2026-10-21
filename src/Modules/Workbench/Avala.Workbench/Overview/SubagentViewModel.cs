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

internal interface ISubagentViewModel : IAgentBranch
{
    JobId Job { get; }

    string Name { get; }

    string Activity { get; }

    string Note { get; }

    bool HasNote { get; }

    string Meter { get; }

    SubagentState State { get; }

    string StateText { get; }

    DateTimeOffset Since { get; }

    bool IsTiming { get; }

    IReadOnlyList<string> Trail { get; }

    bool HasTrail { get; }

    bool IsOpen { get; }

    IRelayCommand ToggleCommand { get; }

    IRelayCommand<JobId> OpenCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class SubagentViewModel(BranchPlaceViewModel place, JobId job) : ISubagentViewModel
{
    public IBranchPlaceViewModel Place => place;

    public JobId Job { get; } = job;

    [ObservableProperty]
    public partial string Name { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    public partial SubagentState State { get; private set; }

    public string StateText => SubagentStates.Text(State);

    [ObservableProperty]
    public partial string Activity { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNote))]
    public partial string Note { get; private set; } = string.Empty;

    public bool HasNote => Note.Length > 0;

    [ObservableProperty]
    public partial string Meter { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial DateTimeOffset Since { get; private set; }

    [ObservableProperty]
    public partial bool IsTiming { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTrail))]
    [NotifyCanExecuteChangedFor(nameof(ToggleCommand))]
    public partial IReadOnlyList<string> Trail { get; private set; } = [];

    public bool HasTrail => Trail.Count > 0;

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    public IRelayCommand<JobId> OpenCommand { get; init; } = OverviewPhrases.Idle;

    [RelayCommand(CanExecute = nameof(HasTrail))]
    private void Toggle() => IsOpen = !IsOpen;

    public void Show(AgentBranch branch, int depth, bool folded)
    {
        place.Show(depth, branch, folded);
        State = branch.State;

        foreach (var entry in branch.Tool.Match<ToolEntry[]>(found => [found], () => []))
        {
            Name = entry.Named;
            Activity = entry.Outcome.IsNone && entry.Doing.Length > 0 ? Shortened(entry.Doing) : Latest(entry.Output);
            Meter = Metered(entry);
            Since = entry.Started.Match(at => at, () => DateTimeOffset.MinValue);
            IsTiming = entry.Outcome.IsNone && entry.Started.IsSome;
            var trail = entry.Trail.AsEnumerable().Reverse().ToList();
            Trail = Trail.SequenceEqual(trail) ? Trail : trail;
        }

        Note = branch.State switch
        {
            SubagentState.Waiting => branch.Asks.Match(target => $"asks: {target}", () => "waits for you"),
            SubagentState.Lost => "its work was lost when its turn or session ended",
            SubagentState.Failed => "it failed",
            SubagentState.Stopped when branch.Tool.Match(tool => tool.Denied, () => false) => "it was denied before it ran",
            _ => string.Empty,
        };
    }

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
