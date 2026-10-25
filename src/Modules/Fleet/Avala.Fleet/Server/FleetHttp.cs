using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Avala.Fleet.Delivering;

namespace Avala.Fleet.Server;

internal sealed record HttpAnswer(ServerAnswer Answer, string Body);

internal sealed class FleetHttp(HttpMessageHandler handler, bool owned) : IDisposable
{
    public static TimeSpan Patience { get; } = TimeSpan.FromSeconds(15);

    private readonly HttpClient client = new(handler, owned) { Timeout = Patience };

    public Task<HttpAnswer> GetAsync(string server, string path, string key, CancellationToken cancellationToken) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(server), path)), key, cancellationToken);

    public Task<HttpAnswer> PostAsync(string server, string path, string key, string body, CancellationToken cancellationToken) =>
        SendAsync(
            new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(server), path)) { Content = new StringContent(body, Encoding.UTF8, "application/json") },
            key,
            cancellationToken);

    public void Dispose() => client.Dispose();

    private static ServerAnswer Answer(HttpStatusCode status) =>
        (int)status switch
        {
            >= 200 and < 300 => ServerAnswer.Answered,
            401 or 403 => ServerAnswer.KeyRefused,
            400 => ServerAnswer.BadRequest,
            404 => ServerAnswer.NotFound,
            _ => ServerAnswer.Unreachable,
        };

    private async Task<HttpAnswer> SendAsync(HttpRequestMessage request, string key, CancellationToken cancellationToken)
    {
        using (request)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

            try
            {
                using var response = await client.SendAsync(request, cancellationToken);

                return new HttpAnswer(Answer(response.StatusCode), await response.Content.ReadAsStringAsync(cancellationToken));
            }
            catch (Exception failure) when (failure is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                return new HttpAnswer(ServerAnswer.Unreachable, string.Empty);
            }
        }
    }
}
