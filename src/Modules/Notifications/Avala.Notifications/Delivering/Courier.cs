using Avala.Notifications.Composing;
using Avala.Notifications.Contracts;
using Avala.Sdk;

namespace Avala.Notifications.Delivering;

internal interface INotificationChannel
{
    ChannelKind Kind { get; }

    Task<Result<OutgoingMessage, NotificationError>> SendAsync(OutgoingMessage message, NotificationSettings settings, CancellationToken cancellationToken);
}

internal interface ISecrets
{
    Option<string> Of(string variable);
}

internal sealed class Courier(IEnumerable<INotificationChannel> channels, ISecrets secrets)
{
    public static IReadOnlyList<TimeSpan> Backoff { get; } = [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2)];

    public static bool Retryable(NotificationError error) => error == NotificationError.Unreachable;

    public SecretStatus SecretOf(NotificationSettings settings) =>
        settings.SecretVariable.Match(
            variable => secrets.Of(variable).Match(value => value.Length > 0, () => false) ? SecretStatus.Set : SecretStatus.NotSet,
            () => SecretStatus.None);

    public Task<Result<OutgoingMessage, NotificationError>> SendAsync(OutgoingMessage message, NotificationSettings settings, CancellationToken cancellationToken) =>
        channels.FirstOrDefault(channel => channel.Kind == settings.Channel) is { } chosen
            ? chosen.SendAsync(message, settings, cancellationToken)
            : Task.FromResult(Result<OutgoingMessage, NotificationError>.Failure(NotificationError.NotSetUp));
}
