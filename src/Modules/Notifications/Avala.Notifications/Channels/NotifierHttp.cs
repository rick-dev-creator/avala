using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Avala.Notifications.Composing;
using Avala.Notifications.Contracts;
using Avala.Sdk;

namespace Avala.Notifications.Channels;

internal sealed class NotifierHttp(HttpMessageHandler handler, bool owned) : IDisposable
{
    public static TimeSpan Patience { get; } = TimeSpan.FromSeconds(15);

    private readonly HttpClient client = new(handler, owned) { Timeout = Patience };

    public async Task<Result<OutgoingMessage, NotificationError>> PostAsync(
        OutgoingMessage message,
        Payload payload,
        IReadOnlyList<KeyValuePair<string, string>> headers,
        Option<AuthenticationHeaderValue> authorization,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, payload.Address)
        {
            Content = new StringContent(payload.Body, Encoding.UTF8, "application/json"),
        };

        foreach (var value in authorization.Match<AuthenticationHeaderValue[]>(present => [present], () => []))
        {
            request.Headers.Authorization = value;
        }

        foreach (var (name, value) in headers)
        {
            request.Headers.Add(name, value);
        }

        try
        {
            using var response = await client.SendAsync(request, cancellationToken);

            return Outcome(response.StatusCode).Match(error => Result<OutgoingMessage, NotificationError>.Failure(error), () => message);
        }
        catch (Exception failure) when (failure is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return NotificationError.Unreachable;
        }
    }

    public void Dispose() => client.Dispose();

    private static Option<NotificationError> Outcome(HttpStatusCode status) =>
        (int)status switch
        {
            >= 200 and < 300 => Option<NotificationError>.None,
            408 or 429 or >= 500 => NotificationError.Unreachable,
            _ => NotificationError.Refused,
        };
}
