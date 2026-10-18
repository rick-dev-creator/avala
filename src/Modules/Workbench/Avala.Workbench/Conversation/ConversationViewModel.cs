using System.Collections.ObjectModel;
using Avala.Components.Status;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Board;
using Avala.Workbench.Sidebar;
using Avala.Workbench.Steering;
using Avala.Workbench.Timeline;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Conversation;

internal interface IConversationViewModel
{
    JobId Job { get; }

    string Title { get; }

    string Place { get; }

    JobStatus Status { get; }

    IStatusPillViewModel Pill { get; }

    IPlanPanelViewModel Plan { get; }

    IComposerViewModel Composer { get; }

    IWorkingViewModel Working { get; }

    IReadOnlyList<object> Entries { get; }

    IConversationSearchViewModel Search { get; }

    IReadOnlyList<int> TurnStarts { get; }

    string Notice { get; }

    IAsyncRelayCommand CopyMarkdownCommand { get; }

    IAsyncRelayCommand ExportCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class ConversationViewModel : IConversationViewModel
{
    private readonly ComposerViewModel composer;
    private readonly TimelineItems items;
    private readonly PlanPanelViewModel plan;
    private readonly TranscriptExport export;
    private readonly StatusPillViewModel pill = new(StatusKind.Working, string.Empty);
    private readonly WorkingViewModel working = new();
    private readonly Dictionary<string, (ITimelineEntry Entry, ITimelineItem Item)> known = [];
    private readonly ConversationSearchViewModel search;
    private Transcript shown = Transcript.Empty;

    public ConversationViewModel(ComposerViewModel composer, TimelineItems items, PlanPanelViewModel plan, TranscriptExport export)
    {
        this.composer = composer;
        this.export = export;
        this.items = items;
        Title = string.Empty;
        Place = string.Empty;
        Notice = string.Empty;
        Entries = [];
        this.plan = plan;
        search = new ConversationSearchViewModel(() => [.. Entries.Select(item => item is ISearchable searchable ? searchable.SearchText : string.Empty)]);
        search.Found += (_, found) => Mark(found);
    }

    public JobId Job => composer.Job;

    public IComposerViewModel Composer => composer;

    public IStatusPillViewModel Pill => pill;

    public IPlanPanelViewModel Plan => plan;

    public IWorkingViewModel Working => working;

    public IConversationSearchViewModel Search => search;

    [ObservableProperty]
    public partial ObservableCollection<ITimelineItem> Entries { get; private set; }

    IReadOnlyList<object> IConversationViewModel.Entries => Entries;

    [ObservableProperty]
    public partial string Title { get; private set; }

    [ObservableProperty]
    public partial string Place { get; private set; }

    [ObservableProperty]
    public partial JobStatus Status { get; private set; }

    [ObservableProperty]
    public partial string Notice { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<int> TurnStarts { get; private set; } = [];

    private MarkdownChoices Choices =>
        new(known.Where(pair => pair.Value.Item is ToolViewModel { IsExpanded: true }).Select(pair => pair.Key).ToHashSet());

    public void Show(BoardJob job)
    {
        Title = FactPhrases.Title(job.Summary.Instruction);
        Place = ConversationPhrases.Place(job.Summary, job.Transcript.Ran);
        Status = job.Status;
        pill.Kind = FactPhrases.Dot(job);
        pill.Text = ConversationPhrases.Pill(job);
        composer.Track(job);
        working.Show(job);

        if (!ReferenceEquals(job.Transcript, shown))
        {
            shown = job.Transcript;
            plan.Show(job.Transcript.Plan);
            Arrange(job.Transcript);
            Mark();
            search.Refresh();
        }

        foreach (var prompt in Entries.OfType<PromptViewModel>())
        {
            prompt.CanEdit = prompt.IsFromPerson && composer.AcceptsMessages;
            prompt.CanResend = prompt.IsFromPerson && job.TakesMessages;
        }

        Checkpoints(job);

        foreach (var ci in Entries.OfType<CiViewModel>())
        {
            ci.Mark(composer.AcceptsMessages, composer.ResendCommand);
        }
    }

    private void Checkpoints(BoardJob job)
    {
        var restorable = job.Status is JobStatus.NeedsHelp or JobStatus.AwaitingReview;
        var closing = new Dictionary<TurnEndViewModel, int>();
        var attempt = 0;
        TurnEndViewModel? last = null;

        foreach (var item in Entries)
        {
            if (item is PromptViewModel prompt)
            {
                Close(closing, last, attempt);
                (attempt, last) = (prompt.Attempt, null);
            }

            last = item as TurnEndViewModel ?? last;
        }

        Close(closing, last, attempt);

        foreach (var end in Entries.OfType<TurnEndViewModel>())
        {
            var checkpoint = closing.TryGetValue(end, out var closed) ? job.Checkpoints.LastOrDefault(taken => taken.Attempt == closed).ToOption() : Option<JobCheckpoint>.None;
            end.Mark(checkpoint, restorable, composer.RestoreCommand);
        }
    }

    private static void Close(Dictionary<TurnEndViewModel, int> closing, TurnEndViewModel? end, int attempt)
    {
        if (end is not null && attempt > 0)
        {
            closing[end] = attempt;
        }
    }

    [RelayCommand]
    private async Task CopyMarkdownAsync(CancellationToken cancellationToken) =>
        Notice = await export.CopyAsync(Title, shown, Choices, cancellationToken) ? string.Empty : ConversationPhrases.NotCopied;

    [RelayCommand]
    private async Task ExportAsync(CancellationToken cancellationToken) =>
        Notice = (await export.ExportAsync(Title, shown, Choices, cancellationToken)).Match(_ => string.Empty, ConversationPhrases.NotExported);

    private void Mark()
    {
        var starts = Enumerable.Range(0, Entries.Count).Where(index => Entries[index] is PromptViewModel or InterjectionViewModel).ToList();

        if (!starts.SequenceEqual(TurnStarts))
        {
            TurnStarts = starts;
        }
    }

    private void Mark(SearchFound found)
    {
        var matched = found.Matches.ToHashSet();
        var current = found.Current >= 0 ? found.Matches[found.Current] : -1;

        for (var index = 0; index < Entries.Count; index++)
        {
            if (Entries[index] is ISearchable searchable)
            {
                searchable.Found = index == current ? FindMark.Current : matched.Contains(index) ? FindMark.Match : FindMark.None;
                _ = index == current && searchable.Unfold();
            }
        }
    }

    private void Arrange(Transcript transcript)
    {
        var arranged = transcript.Entries.Select(Item).Where(item => item.IsShown).ToList();

        if (Entries.Count == 0)
        {
            if (arranged.Count > 0)
            {
                Entries = [.. arranged];
            }

            return;
        }

        var position = 0;

        foreach (var item in arranged)
        {
            if (position >= Entries.Count || !ReferenceEquals(Entries[position], item))
            {
                Entries.Insert(position, item);
            }

            position++;
        }
    }

    private ITimelineItem Item(ITimelineEntry entry)
    {
        if (known.TryGetValue(entry.Key, out var existing))
        {
            if (!ReferenceEquals(existing.Entry, entry))
            {
                existing.Item.Update(entry);
                known[entry.Key] = (entry, existing.Item);
            }

            return existing.Item;
        }

        var item = items.Create(Job, entry);
        known.Add(entry.Key, (entry, item));

        if (item is PromptViewModel prompt)
        {
            prompt.EditRequested += (_, text) => composer.Edit(text);
            prompt.RetryRequested += (_, text) => composer.ResendCommand.Execute(text);
        }

        return item;
    }
}
