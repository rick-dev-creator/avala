using Avala.Sdk;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Overview;

[INotifyPropertyChanged]
internal sealed partial class OverviewViewModel(ConnectionsViewModel connections, DelegationViewModel delegation) : IPage, IActivatable
{
    public string Title => "Overview";

    public ConnectionsViewModel Connections { get; } = connections;

    public DelegationViewModel Delegation { get; } = delegation;

    [ObservableProperty]
    public partial bool ShowsDelegation { get; private set; }

    public void Activate()
    {
        Connections.Activate();
        Delegation.Activate();
    }

    public void Deactivate()
    {
        Connections.Deactivate();
        Delegation.Deactivate();
    }

    [RelayCommand]
    private void ShowConnections() => ShowsDelegation = false;

    [RelayCommand]
    private void ShowDelegation() => ShowsDelegation = true;
}
