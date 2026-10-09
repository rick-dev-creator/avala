using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Avala.Sdk;

namespace Avala.Runtime.Updates;

internal sealed record PublishedRelease(ReleaseVersion Version, Uri Page, bool Draft, bool Prerelease);

internal sealed class ReleaseFeed(HttpClient client) : IDisposable
{
    public static Uri Releases { get; } = new("https://api.github.com/repos/rick-dev-creator/avala/releases?per_page=30");

    public static Uri ReleasesPage { get; } = new("https://github.com/rick-dev-creator/avala/releases");

    public static TimeSpan Patience { get; } = TimeSpan.FromSeconds(15);

    public async Task<Option<IReadOnlyList<PublishedRelease>>> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Releases);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Avala", "1"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var response = await client.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return Option<IReadOnlyList<PublishedRelease>>.Some([]);
            }

            if (!response.IsSuccessStatusCode)
            {
                return Option<IReadOnlyList<PublishedRelease>>.None;
            }

            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);

            return document.RootElement.ValueKind == JsonValueKind.Array
                ? Option<IReadOnlyList<PublishedRelease>>.Some([.. document.RootElement.EnumerateArray().SelectMany(Release)])
                : Option<IReadOnlyList<PublishedRelease>>.None;
        }
        catch (Exception failure) when (failure is HttpRequestException or JsonException or IOException
            || (failure is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return Option<IReadOnlyList<PublishedRelease>>.None;
        }
    }

    public void Dispose() => client.Dispose();

    private static IEnumerable<PublishedRelease> Release(JsonElement release) =>
        release.ValueKind == JsonValueKind.Object
        && Text(release, "tag_name") is { } tag
        && ReleaseVersion.Parse(tag).Match<ReleaseVersion?>(version => version, () => null) is { } version
            ? [new PublishedRelease(version, Page(Text(release, "html_url")), Flag(release, "draft"), Flag(release, "prerelease") || version.IsPrerelease)]
            : [];

    private static Uri Page(string? text) =>
        Uri.TryCreate(text, UriKind.Absolute, out var page) && page.Scheme == Uri.UriSchemeHttps && page.Host == ReleasesPage.Host
            ? page
            : ReleasesPage;

    private static string? Text(JsonElement release, string name) =>
        release.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Flag(JsonElement release, string name) =>
        release.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
