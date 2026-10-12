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

    public ICloneRepositoryViewModel Cloning { get; init; } = new DesignCloneRepositoryViewModel { IsOpen = false };

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

    [ObservableProperty]
    public partial bool IsPublishing { get; set; } = true;

    public IPublishOptionsViewModel Publish { get; init; } = new DesignPublishOptionsViewModel();

    public string Error { get; init; } = string.Empty;

    public IAsyncRelayCommand StartCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand CreateCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IRelayCommand CancelCommand { get; } = new RelayCommand(() => { });
}

[INotifyPropertyChanged]
internal sealed partial class DesignCloneRepositoryViewModel : ICloneRepositoryViewModel
{
    public bool IsOpen { get; init; } = true;

    [ObservableProperty]
    public partial string Url { get; set; } = "git@github.com:octo/shop.git";

    public string Parent { get; init; } = "/home/ada/code";

    [ObservableProperty]
    public partial string Name { get; set; } = "shop";

    public bool IsCloning { get; init; } = true;

    public string Status { get; init; } = "Cloning git@github.com:octo/shop.git into /home/ada/code/shop…";

    public string Error { get; init; } = string.Empty;

    public IAsyncRelayCommand StartCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand ChooseParentCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand CloneCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IRelayCommand CancelCommand { get; } = new RelayCommand(() => { });
}

[INotifyPropertyChanged]
internal sealed partial class DesignPublishOptionsViewModel : IPublishOptionsViewModel
{
    public IReadOnlyList<string> Targets { get; init; } = ["github", "codeberg"];

    [ObservableProperty]
    public partial string Target { get; set; } = "github";

    public string ForgeKind { get; init; } = "GitHub";

    [ObservableProperty]
    public partial string Name { get; set; } = "invoices";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPublic))]
    public partial bool IsPrivate { get; set; } = true;

    public bool IsPublic
    {
        get => !IsPrivate;
        set => IsPrivate = !value;
    }

    public string Problem { get; init; } = string.Empty;
}
