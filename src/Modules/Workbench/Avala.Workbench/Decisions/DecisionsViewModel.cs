using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Globalization;
using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;
using Avala.Workbench.Board;
using Avala.Workbench.Cards;
using Avala.Workbench.Conversation;
using Avala.Workbench.Replies;
using Avala.Workbench.Sidebar;
using Avala.Workbench.Timeline;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Decisions;

[INotifyPropertyChanged]
internal sealed partial class DecisionsViewModel
{
    private readonly HumanReplies replies;
    private readonly TimeProvider time;
    private readonly Dictionary<(JobId, string), DecisionViewModel> known = [];
    private ImmutableDictionary<JobId, BoardJob> shown = ImmutableDictionary<JobId, BoardJob>.Empty;

    public DecisionsViewModel(HumanReplies replies, TimeProvider time)
    {
        this.replies = replies;
        this.time = time;
        Note = string.Empty;
    }

    public string Empty { get; } = "Nothing needs you";

    public ObservableCollection<DecisionViewModel> Items { get; } = [];

    [ObservableProperty]
    public partial bool IsEmpty { get; private set; } = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ChooseCommand), nameof(AnswerCommand), nameof(DenyCommand), nameof(MoveNextCommand), nameof(MovePreviousCommand))]
    public partial DecisionViewModel? Selected { get; set; }

    [ObservableProperty]
    public partial string Note { get; set; }

    public void Show(ImmutableDictionary<JobId, BoardJob> jobs)
    {
        shown = jobs;
        var now = time.GetUtcNow();
        var waiting = jobs.Values
            .SelectMany(job => job.Transcript.Awaiting.Select(entry => (Job: job, Entry: entry, Since: Since(entry))))
            .OrderBy(found => found.Since)
            .ToList();
        var keys = waiting.Select(found => (found.Job.Job, found.Entry.Key)).ToHashSet();

        foreach (var gone in known.Where(pair => !keys.Contains(pair.Key)).ToList())
        {
            known.Remove(gone.Key);
            Items.Remove(gone.Value);
        }

        for (var position = 0; position < waiting.Count; position++)
        {
            var (job, entry, since) = waiting[position];
            var item = Item(job, entry, since);
            item.Update(entry, now);

            if (Items.IndexOf(item) is var at && at != position)
            {
                if (at >= 0)
                {
                    Items.RemoveAt(at);
                }

                Items.Insert(position, item);
            }
        }

        IsEmpty = Items.Count == 0;
        Selected = Selected is { } selected && Items.Contains(selected) ? selected : Items.FirstOrDefault();
    }

    public void Refresh() => Show(shown);

    [RelayCommand(CanExecute = nameof(HasNext))]
    private void MoveNext() => Selected = Items[Items.IndexOf(Selected!) + 1];

    [RelayCommand(CanExecute = nameof(HasPrevious))]
    private void MovePrevious() => Selected = Items[Items.IndexOf(Selected!) - 1];

    [RelayCommand(CanExecute = nameof(IsFormSelected))]
    private void Choose(string number)
    {
        var field = ((FormCardViewModel)Selected!.Card).Fields.FirstOrDefault(field => field.Choices.Count > 0);

        if (field is not null && int.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out var chosen) && chosen >= 1 && chosen <= field.Choices.Count)
        {
            field.Choices[chosen - 1].IsSelected = !field.Choices[chosen - 1].IsSelected || field.Kind == FieldKind.SingleChoice;
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelected))]
    private Task AnswerAsync() => Selected!.Card switch
    {
        PermissionCardViewModel permission => WithNote(permission).AllowCommand.ExecuteAsync(null),
        FormCardViewModel form => form.SubmitCommand.CanExecute(null) ? form.SubmitCommand.ExecuteAsync(null) : Task.CompletedTask,
        _ => Task.CompletedTask,
    };

    [RelayCommand(CanExecute = nameof(HasSelected))]
    private Task DenyAsync() => Selected!.Card switch
    {
        PermissionCardViewModel permission => WithNote(permission).DenyCommand.ExecuteAsync(null),
        FormCardViewModel form => WithNote(form).DeclineCommand.ExecuteAsync(null),
        _ => Task.CompletedTask,
    };

    private PermissionCardViewModel WithNote(PermissionCardViewModel card)
    {
        card.Note = Note;
        Note = string.Empty;

        return card;
    }

    private FormCardViewModel WithNote(FormCardViewModel card)
    {
        card.Note = Note;
        Note = string.Empty;

        return card;
    }

    private bool HasSelected() => Selected is not null;

    private bool IsFormSelected() => Selected?.Card is FormCardViewModel;

    private bool HasNext() => Selected is { } selected && Items.IndexOf(selected) < Items.Count - 1;

    private bool HasPrevious() => Selected is { } selected && Items.IndexOf(selected) > 0;

    private DecisionViewModel Item(BoardJob job, ITimelineEntry entry, DateTimeOffset since)
    {
        if (!known.TryGetValue((job.Job, entry.Key), out var item))
        {
            item = new DecisionViewModel(job.Job, FactPhrases.Title(job.Summary.Instruction), Card(entry), since);
            known.Add((job.Job, entry.Key), item);
        }

        return item;
    }

    private ITimelineItem Card(ITimelineEntry entry) => entry switch
    {
        PermissionEntry permission => new PermissionCardViewModel(permission, replies),
        _ => new FormCardViewModel((FormEntry)entry, replies),
    };

    private static DateTimeOffset Since(ITimelineEntry entry) => entry switch
    {
        PermissionEntry permission => permission.Decision.Match(decision => decision.At, () => DateTimeOffset.MinValue),
        FormEntry form => form.Decision.Match(decision => decision.At, () => DateTimeOffset.MinValue),
        _ => DateTimeOffset.MinValue,
    };
}
