using Avala.Autopilot.Contracts;
using Avala.Budgets.Contracts;
using Avala.Delegation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Notifications.Channels;
using Avala.Notifications.Contracts;
using Avala.Notifications.Delivering;
using Avala.Notifications.Dispatching;
using Avala.Notifications.NotificationFiles;
using Avala.Notifications.Records;
using Avala.Notifications.Storage;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Avala.Notifications;

public sealed class NotificationsPlugin : IPlugin
{
    public PluginInfo Info { get; } = new("avala.notifications", "Notifications");

    public void Register(IPluginRegistrar registrar)
    {
        registrar.Services.TryAddSingleton(TimeProvider.System);
        registrar.Services
            .AddSingleton<INotificationFile, NotificationFileStore>()
            .AddSingleton<SqliteNotificationStore>()
            .AddForwarded<INotificationStore, SqliteNotificationStore>()
            .AddForwarded<IStartupTask, SqliteNotificationStore>()
            .AddSingleton<ISecrets, EnvironmentSecrets>()
            .AddSingleton(Http)
            .AddSingleton<INotificationChannel, NtfyChannel>()
            .AddSingleton<INotificationChannel, WebhookChannel>()
            .AddSingleton<Courier>()
            .AddSingleton<Outlook>()
            .AddSingleton<SentLog>()
            .AddSingleton<Dispatcher>()
            .AddSingleton<NotificationDesk>()
            .AddForwarded<IHandle<PermissionDecided>, NotificationDesk>()
            .AddForwarded<IHandle<FormDecided>, NotificationDesk>()
            .AddForwarded<IHandle<PermissionAnswered>, NotificationDesk>()
            .AddForwarded<IHandle<JobHeld>, NotificationDesk>()
            .AddForwarded<IHandle<BudgetIntervened>, NotificationDesk>()
            .AddForwarded<IHandle<JobProgressed>, NotificationDesk>()
            .AddForwarded<IHandle<LoopEnded>, NotificationDesk>()
            .AddSingleton<NotificationBook>()
            .AddForwarded<INotifications, NotificationBook>();
    }

    private static NotifierHttp Http(IServiceProvider services) =>
        services.GetKeyedService<HttpMessageHandler>(NotificationTransport.Handler) is { } handler
            ? new NotifierHttp(handler, owned: false)
            : new NotifierHttp(new SocketsHttpHandler(), owned: true);
}
