using System.Collections.Immutable;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Avala.Runtime.Tests.Updates;

internal sealed class FakeReleases : HttpMessageHandler
{
    private readonly TaskCompletionSource<Func<HttpResponseMessage>> reply = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ImmutableList<HttpRequestMessage> requests = [];

    private FakeReleases()
    {
    }

    public IReadOnlyList<HttpRequestMessage> Requests => Volatile.Read(ref requests);

    public static FakeReleases Pending() => new();

    public static FakeReleases Replying(HttpStatusCode status, string body = "[]")
    {
        var releases = new FakeReleases();
        releases.Reply(status, body);

        return releases;
    }

    public static FakeReleases Listing(params object[] releases) => Replying(HttpStatusCode.OK, JsonSerializer.Serialize(releases));

    public static FakeReleases Failing() => new FakeReleases().Fail();

    public static object Release(string tag, bool prerelease = false, bool draft = false) =>
        new Dictionary<string, object>
        {
            ["tag_name"] = tag,
            ["html_url"] = $"https://github.com/rick-dev-creator/avala/releases/tag/{tag}",
            ["prerelease"] = prerelease,
            ["draft"] = draft,
        };

    public void Reply(HttpStatusCode status, string body) =>
        reply.TrySetResult(() => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ImmutableInterlocked.Update(ref requests, list => list.Add(request));

        return (await reply.Task.WaitAsync(cancellationToken))();
    }

    private FakeReleases Fail()
    {
        reply.TrySetResult(() => throw new HttpRequestException("No route to host."));

        return this;
    }
}
