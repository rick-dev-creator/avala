using Avala.Notifications.Contracts;
using Avala.Sdk;

namespace Avala.Notifications.Records;

internal interface INotificationFile
{
    string Path { get; }

    ValueTask<NotificationFile> ReadAsync(CancellationToken cancellationToken);

    ValueTask<Result<NotificationFile, NotificationError>> WriteAsync(NotificationSettings settings, CancellationToken cancellationToken);
}
