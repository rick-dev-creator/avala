using Avala.Components.Privacy;
using Avala.Workbench.Accounts;
using Avala.Workbench.ModelChoices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Welcome;

internal sealed class DesignAccountChipViewModel : IAccountChipViewModel
{
    public Personal Label { get; init; } = Personal.Plain("claude-work");

    public string Monogram => AccountPhrases.Monogram(Label.Value);

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
        new DesignAccountChipViewModel { Label = Personal.Login("octocat"), Logo = DesignLogos.Hub },
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

internal sealed class DesignHarnessOptionViewModel : IHarnessOptionViewModel
{
    public string Name { get; init; } = "Auto";

    public string Monogram => IsAuto ? string.Empty : AccountPhrases.Monogram(Name);

    public bool IsAuto => Name == HarnessPhrases.Auto;

    public string Hint { get; init; } = string.Empty;

    public bool IsAttention { get; init; }

    public bool IsChosen { get; init; }

    public IRelayCommand ChooseCommand { get; } = new RelayCommand(() => { });
}

[INotifyPropertyChanged]
internal sealed partial class DesignHarnessChoiceViewModel : IHarnessChoiceViewModel
{
    public bool IsShown { get; init; } = true;

    public string Label { get; init; } = "Auto · claude-work";

    public string Monogram => AccountPhrases.Monogram("claude-work");

    public string Reason { get; init; } = "Most capacity left · 12% of the 5-hour window used";

    public bool IsAttention { get; init; }

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    public IReadOnlyList<IHarnessOptionViewModel> Options { get; init; } =
    [
        new DesignHarnessOptionViewModel { Hint = "Recommended · now claude-work", IsChosen = true },
        new DesignHarnessOptionViewModel { Name = "claude-work", Hint = "12% of the 5-hour window used" },
        new DesignHarnessOptionViewModel { Name = "claude-personal", Hint = "94% of the 5-hour window used", IsAttention = true },
    ];

    public IModelPickerViewModel Models { get; init; } = new DesignModelPickerViewModel { CanChoose = false, Models = ["Default (sonnet)"], Model = "Default (sonnet)", Efforts = ["Default (medium)"], Effort = "Default (medium)", Note = string.Empty };
}
