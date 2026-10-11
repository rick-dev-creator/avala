using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Settings;

internal sealed class DesignNotificationsViewModel : INotificationsViewModel
{
    public string Status { get; init; } = "applied";

    public bool IsSetUp { get; init; } = true;

    public bool CanSetUp => !HasFile;

    public bool HasFile { get; init; } = true;

    public string Channel => "ntfy";

    public string Destination => "https://ntfy.sh/avala-k3j9m2q7xw5rbt4na6ceyd8h";

    public string Secret => "No token: anyone who knows the topic can read what is sent";

    public bool Decision => true;

    public bool Held => true;

    public bool Budget => true;

    public bool Failed => true;

    public bool LoopEnded => true;

    public bool Review => false;

    public bool IsDetailed => false;

    public string Quiet => "22:00–07:00 every day, urgent notices still sent, the rest in one digest after";

    public string Preview =>
        """
        POST https://ntfy.sh/

        {"topic":"avala-k3j9m2q7xw5rbt4na6ceyd8h","title":"A decision needs you · ledger-api","message":"Fix the flaky login test","priority":4,"tags":["avala","decision","bell"]}
        """;

    public string LastDelivery => "Sent a decision at 09:31";

    public string TestResult { get; init; } = string.Empty;

    public string Error => string.Empty;

    public IAsyncRelayCommand SetUpCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand OpenFileCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand<string> ToggleEventCommand { get; } = new AsyncRelayCommand<string>(_ => Task.CompletedTask);

    public IAsyncRelayCommand<string> ChooseDetailCommand { get; } = new AsyncRelayCommand<string>(_ => Task.CompletedTask);

    public IAsyncRelayCommand SendTestCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public Task LoadAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
