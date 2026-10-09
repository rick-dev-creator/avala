using System.Collections.ObjectModel;
using Avala.Agents.Contracts.Connections;
using Avala.Sdk.Presentation;
using Avala.Workbench.Fleet;
using Avala.Workbench.Following;
using Avala.Workbench.Presenting;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Avala.Workbench.Overview;

[INotifyPropertyChanged]
internal sealed partial class ConnectionsViewModel(FleetReader reader, LiveFeed feed) : IPresentation, IDisposable
{
    public event EventHandler<Presented>? Presented
    {
        add => feed.Presented += value;
        remove => feed.Presented -= value;
    }

    public long Revision => feed.Revision;

    public ObservableCollection<ConnectionCardViewModel> Connections { get; } = [];

    [ObservableProperty]
    public partial string FileNote { get; private set; } = string.Empty;

    public Task Following => feed.Following;

    public void Activate() => feed.Start(reader.ReadAsync, Show);

    public void Deactivate() => feed.Stop();

    public void Dispose() => feed.Dispose();

    private void Show(FleetState fleet)
    {
        FileNote = fleet.File == ConnectionFileStatus.Rejected
            ? $"connections.json is rejected: {fleet.Error.Match(error => error.ToString(), () => "invalid")}"
            : string.Empty;
        Connections.ShowOnly(fleet.Connections.Select(connection => new ConnectionCardViewModel(connection)));
    }
}
