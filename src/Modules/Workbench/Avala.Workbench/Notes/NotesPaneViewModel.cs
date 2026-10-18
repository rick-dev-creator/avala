using Avala.Jobs.Contracts;
using Avala.Notes.Contracts;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Sdk.Regions;
using Avala.Workbench.Following;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace Avala.Workbench.Notes;

internal interface INotesPaneViewModel
{
    bool IsOpen { get; }

    INoteBoardViewModel Board { get; }

    IRelayCommand CloseCommand { get; }
}

internal sealed record PlacedNotes(Option<NotePlace> Place, IReadOnlyList<NoteRecord> Notes);

[INotifyPropertyChanged]
internal sealed partial class NotesPaneViewModel
    : INotesPaneViewModel, IPane, IRegionAware<JobId>, IActivatable, IPresentation, IRecipient<NotesPaneRequested>, IDisposable
{
    private readonly NoteBoardViewModel board;
    private readonly LiveFeed feed;
    private readonly IMessenger messenger;
    private readonly IJobCatalog catalog;
    private Option<JobId> job;
    private Option<NotePlace> place;

    public NotesPaneViewModel(NoteBoardViewModel board, LiveFeed feed, IMessenger messenger, IJobCatalog catalog)
    {
        this.board = board;
        this.feed = feed;
        this.messenger = messenger;
        this.catalog = catalog;
        board.Empty = NotePhrases.JobEmpty;
        messenger.RegisterAll(this);
    }

    public event EventHandler<Presented>? Presented
    {
        add => feed.Presented += value;
        remove => feed.Presented -= value;
    }

    public long Revision => feed.Revision;

    public Task Following => feed.Following;

    public INoteBoardViewModel Board => board;

    [ObservableProperty]
    public partial bool IsOpen { get; private set; }

    public void Activate() => feed.Start(ReadAsync, Show);

    public void Deactivate() => feed.Stop();

    public void Dispose()
    {
        feed.Dispose();
        board.Dispose();
        messenger.UnregisterAll(this);
    }

    public void OnRegionContextChanged(Option<JobId> context)
    {
        if (job != context)
        {
            place = Option<NotePlace>.None;
            board.Clear();
        }

        job = context;
        IsOpen = IsOpen && context.IsSome;
        Announce();
        feed.Refresh();
    }

    public void Receive(NotesPaneRequested message)
    {
        if (job == Option<JobId>.Some(message.Job))
        {
            Toggle(!IsOpen);
            feed.Refresh();
        }
    }

    public void Close() => Toggle(false);

    [RelayCommand]
    private void CloseNow() => Toggle(false);

    IRelayCommand INotesPaneViewModel.CloseCommand => CloseNowCommand;

    private async ValueTask<PlacedNotes> ReadAsync(CancellationToken cancellationToken)
    {
        if (!IsOpen || job.Match<JobId?>(found => found, () => null) is not { } viewing)
        {
            return new PlacedNotes(Option<NotePlace>.None, []);
        }

        var known = place.Match(found => found.Job == Option<JobId>.Some(viewing) ? found : Option<NotePlace>.None, () => Option<NotePlace>.None);
        var resolved = known.IsSome
            ? known
            : (await catalog.HistoryAsync(viewing, cancellationToken)).Map(history => NotePlace.OfJob(viewing, history.Summary.Repository));

        return await resolved.Match(
            async found => new PlacedNotes(found, await board.ListAsync(found, cancellationToken)),
            () => Task.FromResult(new PlacedNotes(Option<NotePlace>.None, [])));
    }

    private void Show(PlacedNotes read)
    {
        foreach (var found in read.Place.Match<NotePlace[]>(shown => [shown], () => []).Where(found => found.Job == job))
        {
            place = found;
            board.Show(found, read.Notes);
        }
    }

    private void Toggle(bool open)
    {
        if (IsOpen == open)
        {
            return;
        }

        IsOpen = open;
        Announce();
    }

    private void Announce()
    {
        foreach (var current in job.Match<JobId[]>(found => [found], () => []))
        {
            messenger.Send(new NotesPaneChanged(current, IsOpen));
        }
    }
}
