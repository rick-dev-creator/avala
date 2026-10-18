using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Regions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace Avala.Workbench.Notes;

internal sealed record NotesPaneRequested(JobId Job);

internal sealed record NotesPaneChanged(JobId Job, bool IsOpen);

internal interface INotesToggleViewModel
{
    bool IsShown { get; }

    bool IsOpen { get; }

    IRelayCommand ToggleCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class NotesToggleViewModel : INotesToggleViewModel, IRegionAware<JobId>, IRecipient<NotesPaneChanged>, IDisposable
{
    private readonly IMessenger messenger;
    private Option<JobId> job;

    public NotesToggleViewModel(IMessenger messenger)
    {
        this.messenger = messenger;
        messenger.RegisterAll(this);
    }

    [ObservableProperty]
    public partial bool IsShown { get; private set; }

    [ObservableProperty]
    public partial bool IsOpen { get; private set; }

    public void Dispose() => messenger.UnregisterAll(this);

    public void OnRegionContextChanged(Option<JobId> context)
    {
        job = context;
        IsShown = context.IsSome;
    }

    public void Receive(NotesPaneChanged message)
    {
        if (job == Option<JobId>.Some(message.Job))
        {
            IsOpen = message.IsOpen;
        }
    }

    [RelayCommand]
    private void Toggle()
    {
        foreach (var current in job.Match<JobId[]>(found => [found], () => []))
        {
            messenger.Send(new NotesPaneRequested(current));
        }
    }
}
