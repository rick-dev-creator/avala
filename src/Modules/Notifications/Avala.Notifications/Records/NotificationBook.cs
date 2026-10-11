using System.Security.Cryptography;
using Avala.Notifications.Contracts;
using Avala.Notifications.Delivering;
using Avala.Notifications.Dispatching;
using Avala.Sdk;

namespace Avala.Notifications.Records;

internal sealed class NotificationBook(INotificationFile file, Dispatcher dispatcher, Outlook outlook, Courier courier) : INotifications
{
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz234567";

    private const int TopicLetters = 24;

    public static string RandomTopic() => $"avala-{RandomNumberGenerator.GetString(Alphabet, TopicLetters)}";

    public async ValueTask<NotificationFile> ReadAsync(CancellationToken cancellationToken) => Shown(await file.ReadAsync(cancellationToken));

    public async ValueTask<Result<NotificationFile, NotificationError>> SetUpAsync(CancellationToken cancellationToken) =>
        (await file.ReadAsync(cancellationToken)).Status == NotificationFileStatus.Absent
            ? (await file.WriteAsync(new NotificationSettings(ChannelKind.Ntfy) { Ntfy = new NtfyTarget(NtfyTarget.DefaultServer, RandomTopic()) }, cancellationToken)).Map(Shown)
            : NotificationError.AlreadySetUp;

    public async ValueTask<Result<NotificationFile, NotificationError>> ChooseAsync(NotificationChoices choices, CancellationToken cancellationToken)
    {
        var current = await file.ReadAsync(cancellationToken);

        return await current.Settings.Match(
            async settings => (await file.WriteAsync(
                settings with { Events = [.. NotificationSettings.Choosable.Where(choices.Events.Contains)], Detail = choices.Detail },
                cancellationToken)).Map(Shown),
            () => Task.FromResult(Result<NotificationFile, NotificationError>.Failure(current.Error.Match(error => error, () => NotificationError.NotSetUp))));
    }

    public async ValueTask<Result<NotificationRecord, NotificationError>> SendTestAsync(CancellationToken cancellationToken) =>
        await dispatcher.TestAsync(cancellationToken);

    public IReadOnlyList<NotificationRecord> Recent() => dispatcher.Recent;

    private NotificationFile Shown(NotificationFile read) =>
        read.Settings.Match(
            settings => read with { Secret = courier.SecretOf(settings), Preview = outlook.Preview(settings) },
            () => read);
}
