using Avala.Sdk;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Overview;

internal interface IOverviewViewModel
{
    string Title { get; }

    IConnectionsViewModel Connections { get; }

    IDelegationViewModel Delegation { get; }

    bool ShowsDelegation { get; }

    IRelayCommand ShowConnectionsCommand { get; }

    IRelayCommand ShowDelegationCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class OverviewViewModel(IConnectionsViewModel connections, IDelegationViewModel delegation) : IOverviewViewModel, IPage, IActivatable
{
    public string Title => "Overview";

    public string Icon => "IconOverview";

    public IConnectionsViewModel Connections { get; } = connections;

    public IDelegationViewModel Delegation { get; } = delegation;

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
