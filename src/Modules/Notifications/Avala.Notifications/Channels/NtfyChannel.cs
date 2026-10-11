using System.Net.Http.Headers;
using Avala.Notifications.Composing;
using Avala.Notifications.Contracts;
using Avala.Notifications.Delivering;
using Avala.Sdk;

namespace Avala.Notifications.Channels;

internal sealed class NtfyChannel(NotifierHttp http, ISecrets secrets) : INotificationChannel
{
    public ChannelKind Kind => ChannelKind.Ntfy;

    public Task<Result<OutgoingMessage, NotificationError>> SendAsync(OutgoingMessage message, NotificationSettings settings, CancellationToken cancellationToken) =>
        settings.Ntfy.Match(
            target => Secrets.Named(secrets, target.TokenEnv).Match(
                token => http.PostAsync(message, Payloads.Ntfy(message, target), [], token.Map(value => new AuthenticationHeaderValue("Bearer", value)), cancellationToken),
                error => Task.FromResult(Result<OutgoingMessage, NotificationError>.Failure(error))),
            () => Task.FromResult(Result<OutgoingMessage, NotificationError>.Failure(NotificationError.NotSetUp)));
}

internal static class Secrets
{
    public static Result<Option<string>, NotificationError> Named(ISecrets secrets, Option<string> variable) =>
        variable.Match(
            name => secrets.Of(name).Match(
                value => value.Length > 0 ? Result<Option<string>, NotificationError>.Success(Option<string>.Some(value)) : NotificationError.MissingSecret,
                () => NotificationError.MissingSecret),
            () => Result<Option<string>, NotificationError>.Success(Option<string>.None));
}
