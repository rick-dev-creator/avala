using Avala.Workbench.Accounts;
using Avala.Workbench.ModelChoices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Avala.Workbench.Harnesses;

namespace Avala.Workbench.Welcome;

internal sealed class DesignAccountChipViewModel : IAccountChipViewModel
{
    public string Label { get; init; } = "claude-work";

    public string Monogram => AccountPhrases.Monogram(Label);

    public string Logo { get; init; } = string.Empty;

    public bool HasLogo => Logo.Length > 0;

    public bool IsAttention { get; init; }

    public string FixLabel { get; init; } = string.Empty;

    public IAsyncRelayCommand FixCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);
}

[INotifyPropertyChanged]
internal sealed partial class DesignWelcomeViewModel : IWelcomeViewModel
{
    public bool IsCompact { get; init; }

    public bool CanCompose { get; init; } = true;

    [ObservableProperty]
    public partial string Instruction { get; set; } = string.Empty;

    public string Error { get; init; } = string.Empty;

    public IReadOnlyList<IAccountChipViewModel> Chips { get; init; } =
    [
        new DesignAccountChipViewModel(),
        new DesignAccountChipViewModel { Label = "octocat", Logo = DesignLogos.Hub },
    ];

    public IHarnessChoiceViewModel Harness { get; init; } = new DesignHarnessChoiceViewModel();

    public IAsyncRelayCommand StartCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand OpenFolderCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand CloneCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand NewRepositoryCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand ConnectCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public void ShowBoard(bool hasJobs)
    {
    }

    public Task CheckAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
