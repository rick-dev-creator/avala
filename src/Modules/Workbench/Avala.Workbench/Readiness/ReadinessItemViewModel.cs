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

    bool IsWorking { get; }

    string Error { get; }

    IReadOnlyList<IReadinessAccountViewModel> Accounts { get; }

    string Dismiss { get; }

    IAsyncRelayCommand ActCommand { get; }

    IAsyncRelayCommand DismissCommand { get; }
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
    private Func<CancellationToken, Task> dismiss = _ => Task.CompletedTask;

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
    public partial bool IsWorking { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DismissCommand))]
    public partial string Dismiss { get; private set; } = string.Empty;

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
        IsWorking = step.Working;
        Dismiss = step.Dismiss;
        IsDone = false;
    }

    public void Dismissing(Func<CancellationToken, Task> next) => dismiss = next;

    public void Finish(PersonalPhrase done)
    {
        Text = done;
        Action = string.Empty;
        Command = string.Empty;
        Accounts = [];
        Error = string.Empty;
        IsWorking = false;
        Dismiss = string.Empty;
        IsDone = true;
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task ActAsync(CancellationToken cancellationToken) => act(cancellationToken);

    private bool CanAct() => !IsDone && Action.Length > 0;

    [RelayCommand(CanExecute = nameof(CanDismiss))]
    private Task DismissAsync(CancellationToken cancellationToken) => dismiss(cancellationToken);

    private bool CanDismiss() => !IsDone && Dismiss.Length > 0;
}
