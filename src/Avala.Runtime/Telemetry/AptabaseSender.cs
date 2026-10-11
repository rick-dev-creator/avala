using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Avala.Runtime.Telemetry;

internal sealed partial class AptabaseSender(HttpClient client, TelemetryKey key, ILogger<AptabaseSender> logger) : IDisposable
{
    public const string KeyHeader = "App-Key";

    public static TimeSpan Patience { get; } = TimeSpan.FromSeconds(5);

    public async Task<bool> SendAsync(string payload, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, key.Events)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
            request.Headers.Add(KeyHeader, key.AppKey);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Avala", "1"));
            using var response = await client.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            Refused(logger, (int)response.StatusCode);

            return false;
        }
        catch (Exception failure) when (failure is HttpRequestException or IOException
            || (failure is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            Unreachable(logger, failure.Message);

            return false;
        }
    }

    public void Dispose() => client.Dispose();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Usage statistics were refused with status {Status}; they are sent again at the next start")]
    private static partial void Refused(ILogger logger, int status);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Usage statistics could not be sent: {Reason}; they are sent again at the next start")]
    private static partial void Unreachable(ILogger logger, string reason);
}
