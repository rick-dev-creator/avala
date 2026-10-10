using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Globalization;
using Avala.Agents.Contracts.Events;
using Avala.Components.Keycaps;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Workbench.Board;
using Avala.Workbench.Cards;
using Avala.Workbench.Conversation;
using Avala.Workbench.Navigation;
using Avala.Workbench.Presenting;
using Avala.Workbench.Replies;
using Avala.Workbench.Sidebar;
using Avala.Workbench.Timeline;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Decisions;

internal interface IDecisionsViewModel : IActivatable
{
    event EventHandler? CloseRequested;

    string Empty { get; }

    string Pending { get; }

    IReadOnlyList<IDecisionViewModel> Items { get; }

    bool IsEmpty { get; }

    IDecisionViewModel? Selected { get; set; }

    string Note { get; set; }

    bool IsWritingNote { get; }

    IReadOnlyList<IKeycapHintViewModel> Hints { get; }

    IKeycapHintViewModel CloseHint { get; }

    IRelayCommand MoveNextCommand { get; }

    IRelayCommand MovePreviousCommand { get; }

    IRelayCommand<string> ChooseCommand { get; }

    IAsyncRelayCommand AnswerCommand { get; }

    IAsyncRelayCommand DenyCommand { get; }

    IRelayCommand WriteNoteCommand { get; }

    IRelayCommand CloseCommand { get; }

    void Refresh();
}

[INotifyPropertyChanged]
internal sealed partial class DecisionsViewModel : IDecisionsViewModel, IPresentation, IDisposable
{
    private readonly HumanReplies replies;
    private readonly TimeProvider time;
    private readonly BoardFeed feed;
    private readonly JobFocus focus;
    private readonly Dictionary<(JobId, string), DecisionViewModel> known = [];
    private readonly ObservableCollection<DecisionViewModel> items = [];
    private ImmutableDictionary<JobId, BoardJob> shown = ImmutableDictionary<JobId, BoardJob>.Empty;
    private ITimer? aging;

    public static TimeSpan AgingPeriod { get; } = TimeSpan.FromSeconds(15);

    public DecisionsViewModel(HumanReplies replies, TimeProvider time, BoardFeed feed, JobFocus focus)
    {
        this.replies = replies;
        this.time = time;
        this.feed = feed;
        this.focus = focus;
        Note = string.Empty;
        Pending = "all answered";
        Hints = DecisionHints.Idle;
    }

    public event EventHandler? CloseRequested;

    public string Empty { get; } = "Nothing needs you";

    public IReadOnlyList<IDecisionViewModel> Items => items;

    public IKeycapHintViewModel CloseHint { get; } = DecisionHints.Close;

    public Task Following => feed.Following;

    public long Revision => feed.Revision;

    public event EventHandler<Presented>? Presented
    {
        add => feed.Presented += value;
        remove => feed.Presented -= value;
    }

    [ObservableProperty]
    public partial bool IsEmpty { get; private set; } = true;

    [ObservableProperty]
    public partial string Pending { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<IKeycapHintViewModel> Hints { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ChooseCommand), nameof(AnswerCommand), nameof(DenyCommand), nameof(MoveNextCommand), nameof(MovePreviousCommand), nameof(WriteNoteCommand))]
    public partial IDecisionViewModel? Selected { get; set; }

    [ObservableProperty]
    public partial string Note { get; set; }

    [ObservableProperty]
    public partial bool IsWritingNote { get; private set; }

    public void Activate()
    {
        feed.Start(Show);
        aging ??= time.CreateTimer(Aged, null, AgingPeriod, AgingPeriod);
    }

    public void Deactivate()
    {
        aging?.Dispose();
        aging = null;
        feed.Stop();
    }

    public void Dispose() => Deactivate();

    public IReadOnlyList<Func<CancellationToken, Task>> Show(ImmutableDictionary<JobId, BoardJob> jobs)
    {
        shown = jobs;
        var now = time.GetUtcNow();
        var waiting = jobs.Values
            .SelectMany(job => job.Transcript.Pending.Select(entry => (Job: job, Entry: entry, Since: Since(entry))))
            .GroupBy(found => found.Job.Summary.Parent.Match(parent => parent, () => found.Job.Job))
            .OrderBy(family => family.Min(found => Oldest(found.Since)))
            .SelectMany(family => family.OrderBy(found => found.Job.Summary.Parent.IsSome).ThenBy(found => Oldest(found.Since)))
            .ToList();
        var keys = waiting.Select(found => (found.Job.Job, found.Entry.Key)).ToHashSet();

        foreach (var gone in known.Where(pair => !keys.Contains(pair.Key)).ToList())
        {
            known.Remove(gone.Key);
            gone.Value.Opened -= OnOpened;
            items.Remove(gone.Value);
        }

        for (var position = 0; position < waiting.Count; position++)
        {
            var (job, entry, since) = waiting[position];
            var item = Item(job, entry, since);
            item.Update(entry, now);
            item.Heading = Heading(jobs, job.Summary.Parent, position > 0 ? waiting[position - 1].Job.Summary.Parent : Option<JobId>.None);

            if (items.IndexOf(item) is var at && at != position)
            {
                if (at >= 0)
                {
                    items.Move(at, position);
                }
                else
                {
                    items.Insert(position, item);
                }
            }
        }

        IsEmpty = items.Count == 0;
        Pending = IsEmpty ? "all answered" : string.Create(CultureInfo.InvariantCulture, $"{items.Count} pending");
        Selected = Selected is DecisionViewModel selected && items.Contains(selected) ? selected : items.FirstOrDefault();

        return [];
    }

    public void Refresh() => Show(shown);

    private void Aged(object? state) => _ = feed.ShowAgainAsync(Age);

    private void Age()
    {
        var now = time.GetUtcNow();

        foreach (var item in items)
        {
            item.Age(now);
        }
    }

    partial void OnSelectedChanged(IDecisionViewModel? oldValue, IDecisionViewModel? newValue)
    {
        if (oldValue is DecisionViewModel left)
        {
            left.IsSelected = false;
            left.Note = string.Empty;
        }

        if (newValue is DecisionViewModel chosen)
        {
            chosen.IsSelected = true;
        }

        Note = string.Empty;
        IsWritingNote = false;
        Hints = newValue is null ? DecisionHints.Idle : DecisionHints.For(newValue);
    }

    partial void OnNoteChanged(string value)
    {
        if (Selected is DecisionViewModel selected)
        {
            selected.Note = value;
        }
    }

    [RelayCommand(CanExecute = nameof(HasNext))]
    private void MoveNext() => Selected = items[Position + 1];

    [RelayCommand(CanExecute = nameof(HasPrevious))]
    private void MovePrevious() => Selected = items[Position - 1];

    [RelayCommand(CanExecute = nameof(IsFormSelected))]
    private void Choose(string? number)
    {
        var options = Selected?.Options ?? [];

        if (int.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out var chosen) && chosen >= 1 && chosen <= options.Count)
        {
            var choice = options[chosen - 1].Choice;
            choice.IsSelected = !choice.IsSelected || Selected!.IsSingleChoice;
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelected))]
    private Task AnswerAsync() => AnsweredAsync(Selected?.AnswerCommand);

    [RelayCommand(CanExecute = nameof(HasSelected))]
    private Task DenyAsync() => AnsweredAsync(Selected?.DenyCommand);

    [RelayCommand(CanExecute = nameof(HasSelected))]
    private void WriteNote() => IsWritingNote = true;

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    private async Task AnsweredAsync(IAsyncRelayCommand? command)
    {
        if (command is not null && command.CanExecute(null))
        {
            await command.ExecuteAsync(null);
            Note = string.Empty;
            IsWritingNote = false;
        }
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (sender is DecisionViewModel opened)
        {
            focus.Select(opened.Job);
            Close();
        }
    }

    private int Position => Selected is DecisionViewModel selected ? items.IndexOf(selected) : -1;

    private bool HasSelected() => Selected is not null;

    private bool IsFormSelected() => Selected?.Card is IFormCardViewModel;

    private bool HasNext() => Position >= 0 && Position < items.Count - 1;

    private bool HasPrevious() => Position > 0;

    private DecisionViewModel Item(BoardJob job, ITimelineEntry entry, Option<DateTimeOffset> since)
    {
        if (!known.TryGetValue((job.Job, entry.Key), out var item))
        {
            item = new DecisionViewModel(job.Job, FactPhrases.Title(job.Summary.Instruction), Card(entry), since);
            item.Opened += OnOpened;
            known.Add((job.Job, entry.Key), item);
        }

        return item;
    }

    private ITimelineItem Card(ITimelineEntry entry) => entry switch
    {
        PermissionEntry permission => new PermissionCardViewModel(permission, replies),
        _ => new FormCardViewModel((FormEntry)entry, replies),
    };

    private static string Heading(ImmutableDictionary<JobId, BoardJob> jobs, Option<JobId> parent, Option<JobId> previous) =>
        parent.IsNone || parent == previous
            ? string.Empty
            : DecisionPhrases.Children(parent.Bind(found => jobs.TryGetValue(found, out var board) ? board.Summary.Instruction : Option<string>.None));

    private static DateTimeOffset Oldest(Option<DateTimeOffset> since) => since.Match(found => found, () => DateTimeOffset.MaxValue);

    private static Option<DateTimeOffset> Since(ITimelineEntry entry) => entry switch
    {
        PermissionEntry permission => permission.Decision.Map(decision => decision.At),
        FormEntry form => form.Decision.Map(decision => decision.At),
        _ => Option<DateTimeOffset>.None,
    };
}

internal static class DecisionPhrases
{
    public static string Children(Option<string> parentInstruction) =>
        parentInstruction.Match(instruction => $"Sub-agents of {FactPhrases.Title(instruction)}", () => "Sub-agents of another job");
}

internal static class DecisionHints
{
    public static IKeycapHintViewModel Close { get; } = new KeycapHintViewModel("esc", "close");

    public static IReadOnlyList<IKeycapHintViewModel> Idle { get; } = Of([], "answer");

    public static IReadOnlyList<IKeycapHintViewModel> For(IDecisionViewModel selected) =>
        Of(
            selected.Options.Count switch
            {
                0 => [],
                1 => [new KeycapHintViewModel("1", "choose")],
                var count => [new KeycapHintViewModel(string.Create(CultureInfo.InvariantCulture, $"1–{count}"), "choose")],
            },
            selected.IsPermission ? "allow" : "answer");

    private static IReadOnlyList<IKeycapHintViewModel> Of(IKeycapHintViewModel[] choose, string answer) =>
    [
        new KeycapHintViewModel("J K", "move"),
        .. choose,
        new KeycapHintViewModel("⏎", answer),
        new KeycapHintViewModel("⌫", "deny"),
        new KeycapHintViewModel("⇧⏎", "with a note"),
    ];
}
