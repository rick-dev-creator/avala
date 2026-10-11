using Avala.Notifications.Contracts;
using Avala.Sdk;
using Avala.Workbench.Machine;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Settings;

internal interface INotificationsViewModel
{
    string Status { get; }

    bool IsSetUp { get; }

    bool CanSetUp { get; }

    bool HasFile { get; }

    string Channel { get; }

    string Destination { get; }

    string Secret { get; }

    bool Decision { get; }

    bool Held { get; }

    bool Budget { get; }

    bool Failed { get; }

    bool LoopEnded { get; }

    bool Review { get; }

    bool IsDetailed { get; }

    string Quiet { get; }

    string Preview { get; }

    string LastDelivery { get; }

    string TestResult { get; }

    string Error { get; }

    IAsyncRelayCommand SetUpCommand { get; }

    IAsyncRelayCommand OpenFileCommand { get; }

    IAsyncRelayCommand<string> ToggleEventCommand { get; }

    IAsyncRelayCommand<string> ChooseDetailCommand { get; }

    IAsyncRelayCommand SendTestCommand { get; }

    Task LoadAsync(CancellationToken cancellationToken);
}

[INotifyPropertyChanged]
internal sealed partial class NotificationsViewModel(PhoneNotifications notifications) : INotificationsViewModel
{
    private IReadOnlyList<NotificationKind> events = [];

    [ObservableProperty]
    public partial string Status { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSetUp))]
    [NotifyCanExecuteChangedFor(nameof(SendTestCommand), nameof(ToggleEventCommand), nameof(ChooseDetailCommand))]
    public partial bool IsSetUp { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSetUp))]
    [NotifyCanExecuteChangedFor(nameof(SetUpCommand), nameof(OpenFileCommand))]
    public partial bool HasFile { get; private set; }

    public bool CanSetUp => !HasFile;

    [ObservableProperty]
    public partial string Channel { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Destination { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Secret { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool Decision { get; private set; }

    [ObservableProperty]
    public partial bool Held { get; private set; }

    [ObservableProperty]
    public partial bool Budget { get; private set; }

    [ObservableProperty]
    public partial bool Failed { get; private set; }

    [ObservableProperty]
    public partial bool LoopEnded { get; private set; }

    [ObservableProperty]
    public partial bool Review { get; private set; }

    [ObservableProperty]
    public partial bool IsDetailed { get; private set; }

    [ObservableProperty]
    public partial string Quiet { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Preview { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string LastDelivery { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string TestResult { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Error { get; private set; } = string.Empty;

    public async Task LoadAsync(CancellationToken cancellationToken) => Show(await notifications.ReadAsync(cancellationToken));

    [RelayCommand(CanExecute = nameof(CanSetUp))]
    private async Task SetUpAsync(CancellationToken cancellationToken) => await ChangedAsync(await notifications.SetUpAsync(cancellationToken), cancellationToken);

    [RelayCommand(CanExecute = nameof(HasFile))]
    private async Task OpenFileAsync(CancellationToken cancellationToken)
    {
        var opened = await notifications.OpenAsync(cancellationToken);
        Error = opened.Match(_ => string.Empty, error => SettingsPhrases.Opening(error, notifications.Path));
    }

    [RelayCommand(CanExecute = nameof(IsSetUp))]
    private async Task ToggleEventAsync(string name, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<NotificationKind>(name, out var kind))
        {
            return;
        }

        IReadOnlyList<NotificationKind> chosen = events.Contains(kind) ? [.. events.Where(known => known != kind)] : [.. events, kind];
        await ChangedAsync(await notifications.ChooseAsync(new NotificationChoices(chosen, IsDetailed ? DetailLevel.Detailed : DetailLevel.Minimal), cancellationToken), cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(IsSetUp))]
    private async Task ChooseDetailAsync(string level, CancellationToken cancellationToken)
    {
        if (Enum.TryParse<DetailLevel>(level, out var detail))
        {
            await ChangedAsync(await notifications.ChooseAsync(new NotificationChoices(events, detail), cancellationToken), cancellationToken);
        }
    }

    [RelayCommand(CanExecute = nameof(IsSetUp))]
    private async Task SendTestAsync(CancellationToken cancellationToken)
    {
        var sent = await notifications.SendTestAsync(cancellationToken);
        TestResult = sent.Match(_ => NotificationPhrases.TestSent, error => $"The test was not sent: {NotificationPhrases.Error(error)}.");
        await LoadAsync(cancellationToken);
    }

    private async Task ChangedAsync<T>(Result<T, NotificationError> changed, CancellationToken cancellationToken)
    {
        Error = changed.Match(_ => string.Empty, NotificationPhrases.Sentence);
        await LoadAsync(cancellationToken);
    }

    private void Show(NotificationsState state)
    {
        var file = state.File;
        Status = NotificationPhrases.Status(file);
        HasFile = file.Status != NotificationFileStatus.Absent;
        IsSetUp = file.Settings.IsSome;
        LastDelivery = NotificationPhrases.Last(state.LastDelivery, state.Zone);

        foreach (var settings in file.Settings.Match<NotificationSettings[]>(found => [found], () => []))
        {
            ShowSettings(settings, file);
        }

        if (file.Settings.IsNone)
        {
            ShowNone();
        }
    }

    private void ShowSettings(NotificationSettings settings, NotificationFile file)
    {
        events = settings.Events;
        Channel = NotificationPhrases.Channel(settings);
        Destination = NotificationPhrases.Destination(settings);
        Secret = NotificationPhrases.Secret(settings, file.Secret);
        Decision = events.Contains(NotificationKind.Decision);
        Held = events.Contains(NotificationKind.Held);
        Budget = events.Contains(NotificationKind.Budget);
        Failed = events.Contains(NotificationKind.Failed);
        LoopEnded = events.Contains(NotificationKind.LoopEnded);
        Review = events.Contains(NotificationKind.Review);
        IsDetailed = settings.Detail == DetailLevel.Detailed;
        Quiet = NotificationPhrases.Quiet(settings.Quiet);
        Preview = file.Preview;
    }

    private void ShowNone()
    {
        events = [];
        (Channel, Destination, Secret, Quiet, Preview) = (string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
        (Decision, Held, Budget, Failed, LoopEnded, Review, IsDetailed) = (false, false, false, false, false, false, false);
    }
}
