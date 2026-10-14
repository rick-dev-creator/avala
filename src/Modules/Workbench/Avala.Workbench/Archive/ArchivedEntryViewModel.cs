using System.Globalization;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Workbench.Archiving;
using Avala.Workbench.Following;
using Avala.Workbench.Navigation;
using Avala.Workbench.Presenting;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Archive;

internal interface IArchivedEntryViewModel
{
    bool HasArchived { get; }

    string Count { get; }

    IRelayCommand OpenCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class ArchivedEntryViewModel(ArchivedReader reader, LiveFeed feed, JobFocus focus, ArchivedViewModel page)
    : IArchivedEntryViewModel, IActivatable, IPresentation, IDisposable
{
    public event EventHandler<Presented>? Presented
    {
        add => feed.Presented += value;
        remove => feed.Presented -= value;
    }

    public long Revision => feed.Revision;

    public Task Following => feed.Following;

    [ObservableProperty]
    public partial bool HasArchived { get; private set; }

    [ObservableProperty]
    public partial string Count { get; private set; } = "0";

    public void Activate() => feed.Start(_ => ValueTask.FromResult(reader.Count), Show);

    public void Deactivate() => feed.Stop();

    public void Dispose() => feed.Dispose();

    private void Show(int count)
    {
        Count = count.ToString(CultureInfo.InvariantCulture);
        HasArchived = count > 0;
    }

    [RelayCommand]
    private void Open() => focus.Show(page);
}
