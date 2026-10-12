using System.Text.Json;
using Avala.Forges.Contracts;
using Avala.Sdk;

namespace Avala.GitHub.Api;

internal static class GitHubRepositories
{
    private const int Unprocessable = 422;

    public static string GitHost(Uri api) => api.Host == GitHubForge.Public.Host ? "github.com" : api.Host;

    public static async ValueTask<Result<HostedRepository, ForgeError>> CreateAsync(ForgeContext context, RepositoryDraft draft, CancellationToken cancellationToken) =>
        (await context.Api.SendAsync(
            new ForgeRequest(ForgeMethod.Post, "/user/repos")
            {
                Fields = [new("name", draft.Name)],
                Flags = [new("private", draft.Private)],
                Refusals = [Unprocessable],
            },
            cancellationToken))
            .Bind(response => response.Status == Unprocessable
                ? Json.Read(response.Body, Refusal).Bind(Result<HostedRepository, ForgeError>.Failure)
                : Json.Read(response.Body, Hosted));

    public static async ValueTask<Result<IReadOnlyList<ListedRepository>, ForgeError>> ListAsync(ForgeContext context, CancellationToken cancellationToken) =>
        (await context.Api.SendAsync(new ForgeRequest(ForgeMethod.Get, "/user/repos?sort=pushed&per_page=100"), cancellationToken))
            .Bind(response => Json.Read<IReadOnlyList<ListedRepository>>(response.Body, root => [.. root.EnumerateArray().Select(Listed).OrderByDescending(listed => listed.UpdatedAt)]));

    public static async ValueTask<Result<ForgeAccount, ForgeError>> MeAsync(ForgeContext context, CancellationToken cancellationToken) =>
        (await context.Api.SendAsync(new ForgeRequest(ForgeMethod.Get, "/user"), cancellationToken))
            .Bind(response => Json.Read(response.Body, root => new ForgeAccount(Json.Text(root, "login"))))
            .Bind(account => account.Login.Length > 0 ? Result<ForgeAccount, ForgeError>.Success(account) : ForgeError.Malformed);

    private static ListedRepository Listed(JsonElement repository) =>
        new(
            Hosted(repository),
            repository.GetProperty("private").GetBoolean(),
            repository.TryGetProperty("archived", out var archived) && archived.ValueKind == JsonValueKind.True,
            Json.Moment(repository, "pushed_at").Match(moment => moment, () => Json.Moment(repository, "updated_at").Match(moment => moment, () => DateTimeOffset.MinValue)));

    private static ForgeError Refusal(JsonElement root) =>
        root.TryGetProperty("errors", out var errors)
        && errors.ValueKind == JsonValueKind.Array
        && errors.EnumerateArray().Any(error => Json.Text(error, "message").Contains("already exists", StringComparison.OrdinalIgnoreCase))
            ? ForgeError.NameTaken
            : ForgeError.Rejected;

    private static HostedRepository Hosted(JsonElement repository)
    {
        var link = new Uri(repository.GetProperty("html_url").GetString()!);

        return new HostedRepository(
            new RepositoryAddress(link.Host, Json.Text(repository.GetProperty("owner"), "login"), Json.Text(repository, "name")),
            link,
            Json.Text(repository, "clone_url"),
            Json.Text(repository, "ssh_url"));
    }
}
