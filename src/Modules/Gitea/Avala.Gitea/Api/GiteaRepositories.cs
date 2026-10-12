using System.Globalization;
using System.Text.Json;
using Avala.Forges.Contracts;
using Avala.Sdk;

namespace Avala.Gitea.Api;

internal static class GiteaRepositories
{
    private const int Conflict = 409;

    public static async ValueTask<Result<HostedRepository, ForgeError>> CreateAsync(ForgeContext context, RepositoryDraft draft, CancellationToken cancellationToken) =>
        (await context.Api.SendAsync(
            new ForgeRequest(ForgeMethod.Post, $"{GiteaForge.ApiRoot}/user/repos")
            {
                Fields = [new("name", draft.Name)],
                Flags = [new("private", draft.Private)],
                Refusals = [Conflict],
            },
            cancellationToken))
            .Bind(response => response.Status == Conflict ? ForgeError.NameTaken : Json.Read(response.Body, Hosted));

    public static async ValueTask<Result<IReadOnlyList<ListedRepository>, ForgeError>> ListAsync(ForgeContext context, CancellationToken cancellationToken) =>
        (await context.Api.SendAsync(new ForgeRequest(ForgeMethod.Get, $"{GiteaForge.ApiRoot}/user/repos?limit=50"), cancellationToken))
            .Bind(response => Json.Read<IReadOnlyList<ListedRepository>>(response.Body, root => [.. root.EnumerateArray().Select(Listed).OrderByDescending(listed => listed.UpdatedAt)]));

    public static async ValueTask<Result<ForgeAccount, ForgeError>> MeAsync(ForgeContext context, CancellationToken cancellationToken) =>
        (await context.Api.SendAsync(new ForgeRequest(ForgeMethod.Get, $"{GiteaForge.ApiRoot}/user"), cancellationToken))
            .Bind(response => Json.Read(response.Body, root => new ForgeAccount(Json.Text(root, "login"))))
            .Bind(account => account.Login.Length > 0 ? Result<ForgeAccount, ForgeError>.Success(account) : ForgeError.Malformed);

    private static ListedRepository Listed(JsonElement repository) =>
        new(
            Hosted(repository),
            repository.GetProperty("private").GetBoolean(),
            repository.TryGetProperty("archived", out var archived) && archived.ValueKind == JsonValueKind.True,
            DateTimeOffset.TryParse(Json.Text(repository, "updated_at"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var updated) ? updated : DateTimeOffset.MinValue);

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
