using Avala.Workbench.Accounts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.RepositoryChoice;

internal sealed class DesignCloneSourceViewModel(string name, string login, string logo, bool selected) : ICloneSourceViewModel
{
    public DesignCloneSourceViewModel()
        : this("github", "octocat", DesignLogos.Hub, true)
    {
    }

    public string Name { get; } = name;

    public string Login { get; } = login;

    public string Logo { get; } = logo;

    public bool IsSelected { get; } = selected;

    public IRelayCommand SelectCommand { get; } = new RelayCommand(() => { });
}

internal sealed class DesignRemoteRowViewModel(string name, string owner, bool isPrivate, string updated) : IRemoteRowViewModel
{
    public DesignRemoteRowViewModel()
        : this("shop", "octo", true, "2h")
    {
    }

    public string Name { get; } = name;

    public string Owner { get; } = owner;

    public bool IsPrivate { get; } = isPrivate;

    public bool IsArchived { get; init; }

    public string Updated { get; } = updated;

    public IAsyncRelayCommand CloneCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);
}

[INotifyPropertyChanged]
internal sealed partial class DesignCloneSheetViewModel : ICloneSheetViewModel
{
    public bool IsOpen { get; init; } = true;

    [ObservableProperty]
    public partial string Search { get; set; } = string.Empty;

    public IReadOnlyList<ICloneSourceViewModel> Sources { get; init; } =
    [
        new DesignCloneSourceViewModel(),
        new DesignCloneSourceViewModel("codeberg", "ada", DesignLogos.Cup, false),
    ];

    public IReadOnlyList<IRemoteRowViewModel> Repositories { get; init; } =
    [
        new DesignRemoteRowViewModel(),
        new DesignRemoteRowViewModel("design-system", "acme", false, "3d"),
        new DesignRemoteRowViewModel("ledger", "octo", false, "Mar 2025") { IsArchived = true },
    ];

    public string Address { get; init; } = string.Empty;

    public bool IsLoading { get; init; }

    public bool IsCloning { get; init; }

    public string Status { get; init; } = string.Empty;

    public string Destination { get; init; } = "/home/ada/Projects";

    public string Error { get; init; } = string.Empty;

    public string FixLabel { get; init; } = string.Empty;

    public IConnectSheetViewModel Connect { get; init; } = new DesignConnectSheetViewModel { IsOpen = false };

    public IAsyncRelayCommand StartCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand CloneAddressCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand ChangeDestinationCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand FixCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IRelayCommand CancelCommand { get; } = new RelayCommand(() => { });
}
