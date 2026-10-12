using Avala.Jobs.Contracts;
using Avala.Sdk;
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
        IsWaiting = job.Status.IsWorking && transcript.Awaiting.Any();
        IsShown = job.Status.IsWorking && !Streams(transcript.Entries.LastOrDefault());
        Verb = IsWaiting ? WorkingPhrases.Waiting : WorkingPhrases.Verb(job.Status, OpenTool(transcript));
        ShowsElapsed = transcript.LiveSince.IsSome;
        Since = transcript.LiveSince.Match(since => since, () => Since);
    }

    private static bool Streams(ITimelineEntry? last) =>
        last is MessageEntry { Outcome.IsNone: true } or ReasoningEntry { Outcome.IsNone: true };

    private static Option<ToolEntry> OpenTool(Transcript transcript) =>
        transcript.Entries.OfType<ToolEntry>().LastOrDefault(tool => tool.Outcome.IsNone && tool.Called.IsNone).ToOption();
}

internal static class WorkingPhrases
{
    public const string Waiting = "Waiting for you";

    private static readonly Dictionary<string, string> Participles = new(StringComparer.Ordinal)
    {
        ["Write"] = "Writing",
        ["Edit"] = "Editing",
        ["Run"] = "Running",
        ["Search"] = "Searching",
        ["Fetch"] = "Fetching",
        ["Read"] = "Reading",
        ["Load"] = "Loading",
        ["Find"] = "Finding",
    };

    public static string Verb(JobStatus status, Option<ToolEntry> open) => status switch
    {
        JobStatus.Draft or JobStatus.Preparing => "Starting",
        JobStatus.Checking => "Running the checks",
        _ => open.Match(tool => Doing(Presenting.CommandPhrases.Title(tool.Kind, tool.Title, tool.Input.Match(input => input, () => string.Empty))), () => "Thinking"),
    };

    private static string Doing(string title)
    {
        var space = title.IndexOf(' ', StringComparison.Ordinal);
        var first = space < 0 ? title : title[..space];

        return Participles.TryGetValue(first, out var doing) ? doing + title[first.Length..] : title;
    }
}
