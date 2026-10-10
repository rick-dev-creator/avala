using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Avala.Forges.Contracts;
using Avala.Sdk;

namespace Avala.Forges.Testing;

public sealed record SentRequest(string Method, string PathAndQuery, string? Authorization, string Body);

public sealed class FixtureServer : HttpMessageHandler
{
    private readonly Dictionary<string, Queue<(HttpStatusCode Status, string Body)>> routes = new(StringComparer.Ordinal);
    private readonly List<SentRequest> sent = [];

    public IReadOnlyList<SentRequest> Sent => sent;

    public static Task<string> FixtureAsync(string forge, string name, CancellationToken cancellationToken) =>
        File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", forge, $"{name}.json"), cancellationToken);

    public FixtureServer Route(string method, string pathAndQuery, HttpStatusCode status, params string[] bodies)
    {
        routes[$"{method} {pathAndQuery}"] = new Queue<(HttpStatusCode Status, string Body)>(bodies.Select(body => (status, body)));

        return this;
    }

    public IForgeApi Api(Uri root, string scheme = "Bearer") => new FixtureApi(new HttpClient(this), root, new AuthenticationHeaderValue(scheme, "secret-value"));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var path = request.RequestUri!.PathAndQuery;
        sent.Add(new SentRequest(request.Method.Method, path, request.Headers.Authorization?.ToString(), body));

        return routes.TryGetValue($"{request.Method.Method} {path}", out var replies) && (replies.Count > 1 ? replies.Dequeue() : replies.Peek()) is var route
            ? new HttpResponseMessage(route.Status) { Content = new StringContent(route.Body, Encoding.UTF8, "application/json") }
            : new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("""{ "message": "Not Found" }""", Encoding.UTF8, "application/json") };
    }

    private sealed class FixtureApi(HttpClient client, Uri root, AuthenticationHeaderValue authorization) : IForgeApi
    {
        public async ValueTask<Result<ForgeResponse, ForgeError>> SendAsync(ForgeRequest request, CancellationToken cancellationToken)
        {
            using var message = new HttpRequestMessage(request.Method == ForgeMethod.Post ? HttpMethod.Post : HttpMethod.Get, new Uri($"{root.AbsoluteUri.TrimEnd('/')}{request.Path}"));
            message.Headers.Authorization = authorization;

            if (request.Method == ForgeMethod.Post)
            {
                message.Content = new StringContent(JsonSerializer.Serialize(request.Fields.ToDictionary(field => field.Key, field => field.Value)), Encoding.UTF8, "application/json");
            }

            using var response = await client.SendAsync(message, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            return (int)response.StatusCode switch
            {
                >= 200 and < 300 => new ForgeResponse((int)response.StatusCode, body),
                401 or 403 => ForgeError.Unauthorized,
                404 => ForgeError.NotFound,
                _ => ForgeError.Unreachable,
            };
        }
    }
}

public static class Targets
{
    public static ForgeTarget Of(Uri api) => new(api, new RepositoryAddress("example.com", "octo", "shop"), "git@example.com:octo/shop.git");

    public static PullRequestRef Seven(string link) => new(7, new Uri(link), "avala/job-0b5c", "main");
}
