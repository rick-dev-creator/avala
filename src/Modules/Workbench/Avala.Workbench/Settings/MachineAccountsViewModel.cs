using System.Collections.ObjectModel;
using Avala.Workbench.Accounts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Settings;

internal interface IMachineAccountsViewModel
{
    IReadOnlyList<IMachineConnectionViewModel> Connections { get; }

    IConnectionEditorViewModel Editor { get; }

    string ConnectionsFile { get; }

    ISourceControlViewModel SourceControl { get; }

    string ForgesFile { get; }

    string Error { get; }

    string Notice { get; }

    IAsyncRelayCommand OpenConnectionsCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class MachineAccountsViewModel(
    ConnectionEditorViewModel editor,
    ISourceControlViewModel sourceControl,
    IAsyncRelayCommand openConnections) : IMachineAccountsViewModel
{
    public ObservableCollection<MachineConnectionViewModel> Rows { get; } = [];

    public IReadOnlyList<IMachineConnectionViewModel> Connections => Rows;

    public IConnectionEditorViewModel Editor => editor;

    public ConnectionEditorViewModel Editing => editor;

    public ISourceControlViewModel SourceControl => sourceControl;

    public IAsyncRelayCommand OpenConnectionsCommand => openConnections;

    [ObservableProperty]
    public partial string ConnectionsFile { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ForgesFile { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Error { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Notice { get; set; } = string.Empty;
}
