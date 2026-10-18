using Avala.Components.Privacy;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Readiness;

internal interface IReadinessItemViewModel
{
    ReadinessNeed Need { get; }

    PersonalPhrase Text { get; }

    string Action { get; }

    string Command { get; }

    bool IsDone { get; }

    string Error { get; }

    IReadOnlyList<IReadinessAccountViewModel> Accounts { get; }

    IAsyncRelayCommand ActCommand { get; }
}

internal interface IReadinessAccountViewModel
{
    Personal Login { get; }

    string Tool { get; }

    IAsyncRelayCommand ConnectCommand { get; }
}

internal sealed class ReadinessAccountViewModel(Personal login, string tool, Func<CancellationToken, Task> connect) : IReadinessAccountViewModel
{
    public Personal Login { get; } = login;

    public string Tool { get; } = tool;

    public IAsyncRelayCommand ConnectCommand { get; } = new AsyncRelayCommand(connect);
}

[INotifyPropertyChanged]
internal sealed partial class ReadinessItemViewModel : IReadinessItemViewModel
{
    private Func<CancellationToken, Task> act;

    public ReadinessItemViewModel(ReadinessStep step, Func<CancellationToken, Task> act, IReadOnlyList<IReadinessAccountViewModel> accounts)
    {
        this.act = act;
        Need = step.Need;
        Show(step, act, accounts);
    }

    public ReadinessNeed Need { get; }

    [ObservableProperty]
    public partial PersonalPhrase Text { get; private set; } = PersonalPhrase.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ActCommand))]
    public partial string Action { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Command { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ActCommand))]
    public partial bool IsDone { get; private set; }

    [ObservableProperty]
    public partial string Error { get; set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<IReadinessAccountViewModel> Accounts { get; private set; } = [];

    public void Show(ReadinessStep step, Func<CancellationToken, Task> next, IReadOnlyList<IReadinessAccountViewModel> choices)
    {
        act = next;
        Text = step.Text;
        Action = step.Action;
        Command = step.Command;
        Accounts = choices;
        IsDone = false;
    }

    public void Finish(PersonalPhrase done)
    {
        Text = done;
        Action = string.Empty;
        Command = string.Empty;
        Accounts = [];
        Error = string.Empty;
        IsDone = true;
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task ActAsync(CancellationToken cancellationToken) => act(cancellationToken);

    private bool CanAct() => !IsDone && Action.Length > 0;
}
