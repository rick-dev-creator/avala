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
