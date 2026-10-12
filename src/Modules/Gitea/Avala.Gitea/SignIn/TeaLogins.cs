using System.Text.Json;
using Avala.Forges.Contracts;
using Avala.Gitea.Api;
using Avala.Sdk;
using Avala.Sdk.Processes;

namespace Avala.Gitea.SignIn;

internal sealed class TeaLogins : IForgeLogins
{
    public string Forge => GiteaDialect.Gitea.Id;

    public Option<CliCall> Logins { get; } = new CliCall("tea", ["login", "list", "--output", "json"], CliStatusChannel.Output);

    public Option<string> SignInCommand { get; } = "tea login add";

    public IReadOnlyList<string> TokenVariables { get; } = ["GITEA_TOKEN"];

    public Option<ForgeProbe> Probe { get; } = new ForgeProbe("/api/forgejo/v1/version", GiteaDialect.Forgejo.Id);

    public IReadOnlyList<DetectedLogin> Read(ProcessOutcome outcome) =>
        !outcome.Succeeded ? []
        : Json.Read<IReadOnlyList<DetectedLogin>>(outcome.Output, root => root.ValueKind == JsonValueKind.Array ? [.. root.EnumerateArray().SelectMany(Login)] : [])
            .Match(found => found, _ => []);

    private static IEnumerable<DetectedLogin> Login(JsonElement entry)
    {
        var fields = entry.ValueKind == JsonValueKind.Object
            ? entry.EnumerateObject().Where(field => field.Value.ValueKind == JsonValueKind.String).ToDictionary(field => field.Name.ToLowerInvariant(), field => field.Value.GetString() ?? string.Empty, StringComparer.Ordinal)
            : [];

        return fields.GetValueOrDefault("name") is { Length: > 0 } name
            && Uri.TryCreate(fields.GetValueOrDefault("url"), UriKind.Absolute, out var url)
            && url.Scheme is "https" or "http"
            ? [new DetectedLogin(url, fields.GetValueOrDefault("user") ?? string.Empty) { Reference = name }]
            : [];
    }
}
