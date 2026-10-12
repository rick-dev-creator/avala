using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.RepositoryChoice;

[INotifyPropertyChanged]
internal sealed partial class DesignRepositoryFieldViewModel : IRepositoryFieldViewModel
{
    public IReadOnlyList<string> Repositories { get; init; } = ["/home/ada/code/shop-api", "/home/ada/code/shop-web", "/home/ada/code/ledger"];

    [ObservableProperty]
    public partial string Repository { get; set; } = "/home/ada/notes";

    public string Problem { get; init; } = "This folder is not a git repository yet.";

    public string Offer { get; init; } = RepositoryPhrases.MakeRepository;

    public string Notice { get; init; } = string.Empty;

    public INewRepositoryViewModel Creation { get; init; } = new DesignNewRepositoryViewModel();

    public IAsyncRelayCommand ChooseCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand MakeRepositoryCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);
}

[INotifyPropertyChanged]
internal sealed partial class DesignNewRepositoryViewModel : INewRepositoryViewModel
{
    public bool IsOpen { get; init; } = true;

    public string Parent { get; init; } = "/home/ada/code";

    [ObservableProperty]
    public partial string Name { get; set; } = "invoices";

    public string Error { get; init; } = string.Empty;

    public IAsyncRelayCommand StartCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand CreateCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IRelayCommand CancelCommand { get; } = new RelayCommand(() => { });
}
