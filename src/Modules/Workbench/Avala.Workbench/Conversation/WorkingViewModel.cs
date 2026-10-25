using Avala.Jobs.Contracts;
using Avala.Workbench.Board;
using Avala.Workbench.Steering;
using Avala.Workbench.Timeline;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Avala.Workbench.Conversation;

internal interface IWorkingViewModel
{
    bool IsShown { get; }

    string Verb { get; }

    bool IsWaiting { get; }

    bool ShowsElapsed { get; }

    DateTimeOffset Since { get; }
}

[INotifyPropertyChanged]
internal sealed partial class WorkingViewModel : IWorkingViewModel
{
    [ObservableProperty]
    public partial bool IsShown { get; private set; }

    [ObservableProperty]
    public partial string Verb { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsWaiting { get; private set; }

    [ObservableProperty]
    public partial bool ShowsElapsed { get; private set; }

    [ObservableProperty]
    public partial DateTimeOffset Since { get; private set; }

    public void Show(BoardJob job)
    {
        var transcript = job.Transcript;
        var choosing = job.ProposesChecks && job.Status == JobStatus.Checking;
        IsWaiting = job.Status.IsWorking && (transcript.Awaiting.Any() || choosing);
        IsShown = job.Status.IsWorking && (IsWaiting || !transcript.Entries.Any(Open));
        Verb = choosing ? WorkingPhrases.ChoosingChecks
            : IsWaiting ? WorkingPhrases.Waiting
            : job.FindsChecks && job.Status == JobStatus.Checking ? WorkingPhrases.FindingChecks
            : WorkingPhrases.Verb(job.Status);
        ShowsElapsed = transcript.LiveSince.IsSome;
        Since = transcript.LiveSince.Match(since => since, () => Since);
    }

    private static bool Open(ITimelineEntry entry) =>
        entry is MessageEntry { Outcome.IsNone: true } or ReasoningEntry { Outcome.IsNone: true } or ToolEntry { Outcome.IsNone: true, Called.IsNone: true };
}

internal static class WorkingPhrases
{
    public const string Waiting = "Waiting for you";

    public const string FindingChecks = "Finding how to check the work";

    public const string ChoosingChecks = "Waiting for you to choose its checks";

    public static string Verb(JobStatus status) => status switch
    {
        JobStatus.Draft or JobStatus.Preparing => "Starting",
        JobStatus.Checking => "Running the checks",
        _ => "Thinking",
    };
}
