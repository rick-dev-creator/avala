using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Globalization;
using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Workbench.Board;
using Avala.Workbench.Cards;
using Avala.Workbench.Conversation;
using Avala.Workbench.Presenting;
using Avala.Workbench.Replies;
using Avala.Workbench.Sidebar;
using Avala.Workbench.Timeline;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Decisions;

internal interface IDecisionsViewModel : IActivatable
{
    string Empty { get; }

    IReadOnlyList<IDecisionViewModel> Items { get; }

    bool IsEmpty { get; }

    IDecisionViewModel? Selected { get; set; }

    string Note { get; set; }

    IRelayCommand MoveNextCommand { get; }

    IRelayCommand MovePreviousCommand { get; }

    IRelayCommand<string> ChooseCommand { get; }

    IAsyncRelayCommand AnswerCommand { get; }

    IAsyncRelayCommand DenyCommand { get; }

    void Refresh();
}

[INotifyPropertyChanged]
internal sealed partial class DecisionsViewModel : IDecisionsViewModel, IPresentation, IDisposable
{
    private readonly HumanReplies replies;
    private readonly TimeProvider time;
    private readonly BoardFeed feed;
    private readonly Dictionary<(JobId, string), DecisionViewModel> known = [];
    private readonly ObservableCollection<DecisionViewModel> items = [];
    private ImmutableDictionary<JobId, BoardJob> shown = ImmutableDictionary<JobId, BoardJob>.Empty;

    public DecisionsViewModel(HumanReplies replies, TimeProvider time, BoardFeed feed)
    {
        this.replies = replies;
        this.time = time;
        this.feed = feed;
        Note = string.Empty;
    }

    public string Empty { get; } = "Nothing needs you";

    public IReadOnlyList<IDecisionViewModel> Items => items;

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
    [NotifyCanExecuteChangedFor(nameof(ChooseCommand), nameof(AnswerCommand), nameof(DenyCommand), nameof(MoveNextCommand), nameof(MovePreviousCommand))]
    public partial IDecisionViewModel? Selected { get; set; }

    [ObservableProperty]
    public partial string Note { get; set; }

    public void Activate() => feed.Start(Show);

    public void Deactivate() => feed.Stop();

    public void Dispose() => Deactivate();

    public IReadOnlyList<Func<CancellationToken, Task>> Show(ImmutableDictionary<JobId, BoardJob> jobs)
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
            items.Remove(gone.Value);
        }

        for (var position = 0; position < waiting.Count; position++)
        {
            var (job, entry, since) = waiting[position];
            var item = Item(job, entry, since);
            item.Update(entry, now);

            if (items.IndexOf(item) is var at && at != position)
            {
                if (at >= 0)
                {
                    items.RemoveAt(at);
                }

                items.Insert(position, item);
            }
        }

        IsEmpty = items.Count == 0;
        Selected = Selected is DecisionViewModel selected && items.Contains(selected) ? selected : items.FirstOrDefault();

        return [];
    }

    public void Refresh() => Show(shown);

    [RelayCommand(CanExecute = nameof(HasNext))]
    private void MoveNext() => Selected = items[Position + 1];

    [RelayCommand(CanExecute = nameof(HasPrevious))]
    private void MovePrevious() => Selected = items[Position - 1];

    [RelayCommand(CanExecute = nameof(IsFormSelected))]
    private void Choose(string? number)
    {
        var field = (Selected?.Card as IFormCardViewModel)?.Fields.FirstOrDefault(field => field.Choices.Count > 0);

        if (field is not null && int.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out var chosen) && chosen >= 1 && chosen <= field.Choices.Count)
        {
            field.Choices[chosen - 1].IsSelected = !field.Choices[chosen - 1].IsSelected || field.Kind == FieldKind.SingleChoice;
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelected))]
    private Task AnswerAsync() => Selected?.Card switch
    {
        IPermissionCardViewModel permission => WithNote(permission).AllowCommand.ExecuteAsync(null),
        IFormCardViewModel form => form.SubmitCommand.CanExecute(null) ? form.SubmitCommand.ExecuteAsync(null) : Task.CompletedTask,
        _ => Task.CompletedTask,
    };

    [RelayCommand(CanExecute = nameof(HasSelected))]
    private Task DenyAsync() => Selected?.Card switch
    {
        IPermissionCardViewModel permission => WithNote(permission).DenyCommand.ExecuteAsync(null),
        IFormCardViewModel form => WithNote(form).DeclineCommand.ExecuteAsync(null),
        _ => Task.CompletedTask,
    };

    private int Position => Selected is DecisionViewModel selected ? items.IndexOf(selected) : -1;

    private IPermissionCardViewModel WithNote(IPermissionCardViewModel card)
    {
        card.Note = Note;
        Note = string.Empty;

        return card;
    }

    private IFormCardViewModel WithNote(IFormCardViewModel card)
    {
        card.Note = Note;
        Note = string.Empty;

        return card;
    }

    private bool HasSelected() => Selected is not null;

    private bool IsFormSelected() => Selected?.Card is IFormCardViewModel;

    private bool HasNext() => Position >= 0 && Position < items.Count - 1;

    private bool HasPrevious() => Position > 0;

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
