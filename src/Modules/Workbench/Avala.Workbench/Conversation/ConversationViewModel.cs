using System.Collections.ObjectModel;
using System.Globalization;
using Avala.Jobs.Contracts;
using Avala.Workbench.Board;
using Avala.Workbench.Sidebar;
using Avala.Workbench.Timeline;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Avala.Workbench.Conversation;

internal interface IConversationViewModel
{
    JobId Job { get; }

    string Title { get; }

    JobStatus Status { get; }

    string Plan { get; }

    IComposerViewModel Composer { get; }

    IReadOnlyList<object> Entries { get; }
}

[INotifyPropertyChanged]
internal sealed partial class ConversationViewModel : IConversationViewModel
{
    private readonly ComposerViewModel composer;
    private readonly TimelineItems items;
    private readonly Dictionary<string, (ITimelineEntry Entry, ITimelineItem Item)> known = [];
    private Transcript shown = Transcript.Empty;

    public ConversationViewModel(JobId job, ComposerViewModel composer, TimelineItems items)
    {
        Job = job;
        this.composer = composer;
        this.items = items;
        Title = string.Empty;
        Plan = string.Empty;
    }

    public JobId Job { get; }

    public IComposerViewModel Composer => composer;

    public ObservableCollection<ITimelineItem> Entries { get; } = [];

    IReadOnlyList<object> IConversationViewModel.Entries => Entries;

    [ObservableProperty]
    public partial string Title { get; private set; }

    [ObservableProperty]
    public partial JobStatus Status { get; private set; }

    [ObservableProperty]
    public partial string Plan { get; private set; }

    public void Show(BoardJob job)
    {
        Title = FactPhrases.Title(job.Summary.Instruction);
        Status = job.Status;
        composer.Track(job.Status);

        if (ReferenceEquals(job.Transcript, shown))
        {
            return;
        }

        shown = job.Transcript;
        Plan = job.Transcript.Plan.Match(
            plan => string.Create(CultureInfo.InvariantCulture, $"{plan.Done} of {plan.Total}"),
            () => string.Empty);
        Place(job.Transcript);
    }

    private void Place(Transcript transcript)
    {
        var position = 0;

        foreach (var entry in transcript.Entries)
        {
            var item = Item(entry);

            if (!item.IsShown)
            {
                continue;
            }

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

        var item = items.Create(entry);
        known.Add(entry.Key, (entry, item));

        return item;
    }
}
