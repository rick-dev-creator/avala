using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Accounts;

internal static class DesignLogos
{
    public const string Hub = "M8 0C12.42 0 16 3.58 16 8A8 8 0 0 1 0 8C0 3.58 3.58 0 8 0ZM5 6A1 1 0 1 0 5 8A1 1 0 1 0 5 6ZM11 6A1 1 0 1 0 11 8A1 1 0 1 0 11 6ZM4.5 10.5H11.5A3.5 3.5 0 0 1 4.5 10.5Z";

    public const string Cup = "F0 M1.5 4.5H11.5V9A4 4 0 0 1 7.5 13H5.5A4 4 0 0 1 1.5 9ZM5.2 6.4H7.6V8.2H9.4V10.6H5.2Z";
}

internal sealed class DesignAccountRowViewModel : IAccountRowViewModel
{
    public string Name { get; init; } = "github";

    public string Login { get; init; } = "octocat";

    public string Host { get; init; } = "github.com";

    public string SignIn { get; init; } = "gh";

    public string Logo { get; init; } = DesignLogos.Hub;

    public AccountState State { get; init; } = AccountState.SignedIn;

    public bool IsDetected => State == AccountState.Detected;

    public string Problem { get; init; } = string.Empty;

    public IAsyncRelayCommand AddCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand RemoveCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);
}

internal sealed class DesignForgeKindViewModel(string id, string name, string logo, bool selected) : IForgeKindViewModel
{
    public DesignForgeKindViewModel()
        : this("github", "GitHub", DesignLogos.Hub, true)
    {
    }

    public string Id { get; } = id;

    public string Name { get; } = name;

    public string Logo { get; } = logo;

    public bool IsSelected { get; } = selected;

    public IRelayCommand SelectCommand { get; } = new RelayCommand(() => { });
}

[INotifyPropertyChanged]
internal sealed partial class DesignConnectSheetViewModel : IConnectSheetViewModel
{
    public bool IsOpen { get; init; } = true;

    public IReadOnlyList<IForgeKindViewModel> Kinds { get; init; } =
    [
        new DesignForgeKindViewModel(),
        new DesignForgeKindViewModel("gitea", "Gitea", DesignLogos.Cup, false),
        new DesignForgeKindViewModel("forgejo", "Forgejo", DesignLogos.Cup, false),
    ];

    public bool NeedsHost { get; init; }

    [ObservableProperty]
    public partial string Host { get; set; } = string.Empty;

    public bool OffersCli { get; init; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsesToken))]
    public partial bool UsesCli { get; set; } = true;

    public bool UsesToken
    {
        get => !UsesCli;
        set => UsesCli = !value;
    }

    public string Cli { get; init; } = "gh";

    public CliState CliState { get; init; } = CliState.SignedOut;

    public string CliStatus { get; init; } = AccountPhrases.Cli(CliState.SignedOut, "gh", string.Empty);

    public string SignInCommand { get; init; } = "gh auth login";

    [ObservableProperty]
    public partial string TokenVariable { get; set; } = "GITHUB_TOKEN";

    public string Error { get; init; } = string.Empty;

    public IAsyncRelayCommand ConnectCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand CheckAgainCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IRelayCommand CancelCommand { get; } = new RelayCommand(() => { });
}

internal sealed class DesignSourceControlViewModel : ISourceControlViewModel
{
    public IReadOnlyList<IAccountRowViewModel> Accounts { get; init; } =
    [
        new DesignAccountRowViewModel(),
        new DesignAccountRowViewModel { Name = "work", Login = "work", Host = "github.com", SignIn = AccountPhrases.Token, State = AccountState.Attention, Problem = "Token not set" },
        new DesignAccountRowViewModel { Name = "codeberg", Login = "ada", Host = "codeberg.org", SignIn = "tea", Logo = DesignLogos.Cup, State = AccountState.Detected },
    ];

    public IConnectSheetViewModel Sheet { get; init; } = new DesignConnectSheetViewModel { IsOpen = false };

    public bool IsChecking => false;

    public string Error => string.Empty;

    public IAsyncRelayCommand ConnectCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand CheckAgainCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public Task LoadAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
