using Avala.Notifications.Contracts;
using Avala.Sdk;

namespace Avala.Workbench.Machine;

internal sealed record NotificationsState(NotificationFile File, Option<NotificationRecord> LastDelivery, TimeZoneInfo Zone);

internal sealed class PhoneNotifications(INotifications notifications, SettingsFiles files, TimeProvider clock)
{
    public const string File = "notifications.json";

    public string Path => files.InDataFolder(File);

    public async ValueTask<NotificationsState> ReadAsync(CancellationToken cancellationToken) =>
        new(
            await notifications.ReadAsync(cancellationToken),
            notifications.Recent().LastOrDefault(record => record.Outcome is NotificationOutcome.Sent or NotificationOutcome.Failed).ToOption(),
            clock.LocalTimeZone);

    public ValueTask<Result<NotificationFile, NotificationError>> SetUpAsync(CancellationToken cancellationToken) => notifications.SetUpAsync(cancellationToken);

    public ValueTask<Result<NotificationFile, NotificationError>> ChooseAsync(NotificationChoices choices, CancellationToken cancellationToken) =>
        notifications.ChooseAsync(choices, cancellationToken);

    public ValueTask<Result<NotificationRecord, NotificationError>> SendTestAsync(CancellationToken cancellationToken) => notifications.SendTestAsync(cancellationToken);

    public ValueTask<Result<OpenedFile, FileOpenError>> OpenAsync(CancellationToken cancellationToken) => files.OpenInDataFolderAsync(File, cancellationToken);
}
