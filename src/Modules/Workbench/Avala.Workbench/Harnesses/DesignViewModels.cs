using Avala.Workbench.Accounts;
using Avala.Workbench.ModelChoices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Harnesses;

internal sealed class DesignHarnessOptionViewModel : IHarnessOptionViewModel
{
    public string Name { get; init; } = "Auto";

    public string Connection { get; init; } = string.Empty;

    public string Monogram => IsAuto ? string.Empty : AccountPhrases.Monogram(Connection.Length > 0 ? Connection : Name);

    public bool IsAuto => Name == "Auto";

    public string Hint { get; init; } = string.Empty;

    public bool IsAttention { get; init; }

    public bool IsChosen { get; init; }

    public bool IsAvailable { get; init; } = true;

    public IRelayCommand ChooseCommand { get; } = new RelayCommand(() => { });
}

[INotifyPropertyChanged]
internal sealed partial class DesignHarnessChoiceViewModel : IHarnessChoiceViewModel
{
    public bool IsShown { get; init; } = true;

    public string Heading { get; init; } = "Run on";

    public string Note { get; init; } = string.Empty;

    public bool OpensUpward { get; init; }

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

    public IModelPickerViewModel Models { get; init; } = new DesignModelPickerViewModel { IsShown = false, CanChoose = false, Models = ["Default (sonnet)"], Model = "Default (sonnet)", Efforts = ["Default (medium)"], Effort = "Default (medium)", Note = string.Empty };
}
