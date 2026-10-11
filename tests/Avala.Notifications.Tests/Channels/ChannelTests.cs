using System.Net;
using Avala.Notifications.Channels;
using Avala.Notifications.Composing;
using Avala.Notifications.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Notifications.Tests.Channels;

public sealed class ChannelTests : IDisposable
{
    private static readonly OutgoingMessage Message = Composer.Compose([Samples.Decision(Samples.Job)], DetailLevel.Minimal, text => text, Samples.Monday);

    private static readonly NotificationSettings Ntfy = Notified.Ntfy with { Ntfy = new NtfyTarget(new Uri("https://ntfy.example.com"), "avala-k3j9") { TokenEnv = "NTFY_TOKEN" } };

    private static readonly NotificationSettings Webhook = new(ChannelKind.Webhook) { Webhook = new WebhookTarget(new Uri("https://hooks.example.com/avala")) { SecretEnv = "HOOK_SECRET" } };

    private readonly FakeNotifierEndpoint endpoint = new();
    private readonly FakeSecrets secrets = new();

    [Fact]
    public async Task AnNtfyMessageIsPostedToTheServerWithTheTokenItsVariableHoldsAsync()
    {
        secrets.Values["NTFY_TOKEN"] = "tk_live";
        using var http = new NotifierHttp(endpoint, owned: false);

        Outcomes.Succeeds(await new NtfyChannel(http, secrets).SendAsync(Message, Ntfy, Notified.Cancellation));

        var request = Assert.Single(endpoint.Received);
        Assert.Equal(("POST", "https://ntfy.example.com/", "Bearer tk_live"), (request.Method, request.Address.AbsoluteUri, request.Headers["Authorization"]));
        Assert.Equal(Payloads.Ntfy(Message, Outcomes.Present(Ntfy.Ntfy)).Body, request.Body);
    }

    [Fact]
    public async Task ATokenVariableThatIsNotSetSendsNothingAsync()
    {
        using var http = new NotifierHttp(endpoint, owned: false);

        var sent = await new NtfyChannel(http, secrets).SendAsync(Message, Ntfy, Notified.Cancellation);

        Assert.Equal(NotificationError.MissingSecret, Outcomes.FailsWith(sent));
        Assert.Empty(endpoint.Received);
    }

    [Fact]
    public async Task AWebhookIsSignedWithTheSecretOverItsTimestampNonceAndBodyAsync()
    {
        secrets.Values["HOOK_SECRET"] = "s3cret";
        using var http = new NotifierHttp(endpoint, owned: false);

        Outcomes.Succeeds(await new WebhookChannel(http, secrets, new FakeTimeProvider(Samples.Monday)).SendAsync(Message, Webhook, Notified.Cancellation));

        var request = Assert.Single(endpoint.Received);
        var (timestamp, nonce) = (request.Headers["X-Avala-Timestamp"], request.Headers["X-Avala-Nonce"]);
        var expected = Payloads.Signature("s3cret", timestamp, nonce, request.Body);
        Assert.Equal((Samples.Monday.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture), expected), (timestamp, request.Headers["X-Avala-Signature"]));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, NotificationError.Unreachable)]
    [InlineData(HttpStatusCode.TooManyRequests, NotificationError.Unreachable)]
    [InlineData(HttpStatusCode.Forbidden, NotificationError.Refused)]
    public async Task AnAnswerOutsideSuccessIsRetryableOnlyWhenTheServerMayRecoverAsync(HttpStatusCode status, NotificationError error)
    {
        using var http = new NotifierHttp(endpoint.Replying(status), owned: false);

        var sent = await new WebhookChannel(http, secrets, new FakeTimeProvider(Samples.Monday)).SendAsync(Message, Webhook with { Webhook = Webhook.Webhook.Map(target => target with { SecretEnv = Option<string>.None }) }, Notified.Cancellation);

        Assert.Equal(error, Outcomes.FailsWith(sent));
    }

    [Fact]
    public async Task AServerThatCannotBeReachedIsUnreachableAsync()
    {
        endpoint.Unreachable = true;
        using var http = new NotifierHttp(endpoint, owned: false);

        var sent = await new NtfyChannel(http, secrets).SendAsync(Message, Notified.Ntfy, Notified.Cancellation);

        Assert.Equal(NotificationError.Unreachable, Outcomes.FailsWith(sent));
    }

    public void Dispose() => endpoint.Dispose();
}
