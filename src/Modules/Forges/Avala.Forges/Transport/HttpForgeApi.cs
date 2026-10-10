using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Avala.Forges.Contracts;
using Avala.Sdk;

namespace Avala.Forges.Transport;

internal sealed record ForgeToken(string Scheme, string Variable);

internal sealed class HttpForgeApi(HttpClient client, Uri root, Option<ForgeToken> token, Func<string, Option<string>> environment) : IForgeApi
{
    private const string JsonMedia = "application/json";

    public async ValueTask<Result<ForgeResponse, ForgeError>> SendAsync(ForgeRequest request, CancellationToken cancellationToken)
    {
        var authorization = token.Match(
            named => environment(named.Variable).Match(
                value => value.Length > 0
                    ? Result<Option<AuthenticationHeaderValue>, ForgeError>.Success(new AuthenticationHeaderValue(named.Scheme, value))
                    : ForgeError.MissingCredential,
                () => ForgeError.MissingCredential),
            () => Option<AuthenticationHeaderValue>.None);

        return await authorization.Match(
            header => SendAsync(request, header, cancellationToken),
            error => Task.FromResult(Result<ForgeResponse, ForgeError>.Failure(error)));
    }

    public static Uri Address(Uri root, string path) => new($"{root.AbsoluteUri.TrimEnd('/')}{path}");

    public static string Json(IReadOnlyList<KeyValuePair<string, string>> fields)
    {
        using var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();

            foreach (var (name, value) in fields)
            {
                writer.WriteString(name, value);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private async Task<Result<ForgeResponse, ForgeError>> SendAsync(ForgeRequest request, Option<AuthenticationHeaderValue> authorization, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(request.Method == ForgeMethod.Post ? HttpMethod.Post : HttpMethod.Get, Address(root, request.Path));
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(JsonMedia));
        message.Headers.UserAgent.Add(new ProductInfoHeaderValue("Avala", "1"));
        message.Headers.Authorization = authorization.Match<AuthenticationHeaderValue?>(header => header, () => null);

        if (request.Method == ForgeMethod.Post)
        {
            message.Content = new StringContent(Json(request.Fields), Encoding.UTF8, JsonMedia);
        }

        try
        {
            using var response = await client.SendAsync(message, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var exhausted = response.Headers.TryGetValues("x-ratelimit-remaining", out var remaining) && remaining.Contains("0");

            return Statuses.Of((int)response.StatusCode, body, exhausted);
        }
        catch (HttpRequestException)
        {
            return ForgeError.Unreachable;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ForgeError.Unreachable;
        }
    }
}

internal static class Statuses
{
    public static Result<ForgeResponse, ForgeError> Of(int status, string body, bool rateLimitExhausted) => status switch
    {
        >= 200 and < 300 => new ForgeResponse(status, body),
        401 => ForgeError.Unauthorized,
        403 when rateLimitExhausted => ForgeError.RateLimited,
        403 => ForgeError.Unauthorized,
        404 => ForgeError.NotFound,
        429 => ForgeError.RateLimited,
        409 or 422 => ForgeError.Rejected,
        _ => ForgeError.Unreachable,
    };
}
