using Avala.Jobs.Contracts;
using Avala.Notifications.Contracts;
using Avala.Notifications.Notices;

namespace Avala.Notifications.Composing;

internal static class Previews
{
    public static Notice Sample(DateTimeOffset at) =>
        new(NotificationKind.Decision, "preview", "preview", at)
        {
            Job = new JobId(Guid.Empty),
            Repository = "ledger-api",
            Title = "Fix the flaky login test",
            Detail = "Command: dotnet test --filter Login",
        };

    public static string Of(NotificationSettings settings, Func<string, string> redact, DateTimeOffset at)
    {
        var message = Composer.Compose([Sample(at)], settings.Detail, redact, at);

        return settings.Channel == ChannelKind.Ntfy
            ? settings.Ntfy.Match(target => Shown(Payloads.Ntfy(message, target), []), () => string.Empty)
            : settings.Webhook.Match(
                target => Shown(
                    Payloads.Webhook(message, target),
                    target.SecretEnv.Match<string[]>(_ => [$"{Payloads.SignatureHeader}: sha256=…"], () => [])),
                () => string.Empty);
    }

    private static string Shown(Payload payload, IReadOnlyList<string> headers) =>
        string.Join('\n', [$"POST {payload.Address.AbsoluteUri}", .. headers, string.Empty, payload.Body]);
}
