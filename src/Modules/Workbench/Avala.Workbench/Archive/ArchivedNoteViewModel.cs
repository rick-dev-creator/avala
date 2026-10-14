using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Sdk.Regions;
using Avala.Workbench.Archiving;
using Avala.Workbench.Following;
using Avala.Workbench.Presenting;
using Avala.Workbench.Projects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Archive;

internal interface IArchivedNoteViewModel
{
    bool IsShown { get; }

    IAsyncRelayCommand RestoreCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class ArchivedNoteViewModel(ArchivedReader reader, LiveFeed feed, ProjectShelf shelf)
    : IArchivedNoteViewModel, IRegionAware<JobId>, IActivatable, IPresentation, IDisposable
{
    private Option<JobId> job;

    public event EventHandler<Presented>? Presented
    {
        add => feed.Presented += value;
        remove => feed.Presented -= value;
    }

    public long Revision => feed.Revision;

    public Task Following => feed.Following;

    [ObservableProperty]
    public partial bool IsShown { get; private set; }

    public void Activate() => feed.Start(_ => ValueTask.FromResult(job.Match(reader.Holds, () => false)), shown => IsShown = shown);

    public void Deactivate() => feed.Stop();

    public void Dispose() => feed.Dispose();

    public void OnRegionContextChanged(Option<JobId> context)
    {
        job = context;
        feed.Refresh();
    }

    [RelayCommand]
    private async Task RestoreAsync(CancellationToken cancellationToken)
    {
        if (job.Match<JobId?>(found => found, () => null) is { } shown)
        {
            await shelf.RestoreAsync(shown, cancellationToken);
        }
    }
}
