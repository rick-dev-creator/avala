using Avala.Notifications.NotificationFiles;
using Avala.Notifications.Records;
using Avala.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Notifications;

public sealed class NotificationsPlugin : IPlugin
{
    public PluginInfo Info { get; } = new("avala.notifications", "Notifications");

    public void Register(IPluginRegistrar registrar) =>
        registrar.Services.AddSingleton<INotificationFile, NotificationFileStore>();
}
