using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace Avala.Server.Tests;

public sealed partial class DeploymentTests
{
    private static readonly string Root = RootOf(AppContext.BaseDirectory);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheComposeFileSetsEverySettingTheServerRequiresAsync()
    {
        var services = await ServicesAsync();
        var server = (YamlMappingNode)services["server"];
        var environment = ((YamlMappingNode)server["environment"]).Children.Keys.Select(key => key.ToString());

        Assert.Subset(new HashSet<string>(environment, StringComparer.Ordinal), new HashSet<string>(["ConnectionStrings__fleet", "Authentication__AdministratorKey"], StringComparer.Ordinal));
    }

    [Fact]
    public async Task TheComposeImageIsTheOneTheServerProjectBuildsAsync()
    {
        var services = await ServicesAsync();
        var project = await File.ReadAllTextAsync(Path.Combine(Root, "backend", "Fleet", "Avala.Server", "Avala.Server.csproj"), Cancellation);
        var repository = Repository().Match(project).Groups["name"].Value;

        Assert.Contains($"/{repository}}}:", ((YamlMappingNode)services["server"])["image"].ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheComposeRunsThePostgresTheIntegrationTestsUseAsync()
    {
        var services = await ServicesAsync();
        var fixture = await File.ReadAllTextAsync(Path.Combine(Root, "backend", "tests", "Avala.Server.Activity.IntegrationTests", "PostgresFixture.cs"), Cancellation);

        Assert.Equal(Image().Match(fixture).Groups["image"].Value, ((YamlMappingNode)services["postgres"])["image"].ToString());
    }

    [Fact]
    public async Task CaddyReceivesTheDomainAndTheTlsModeItsCaddyfileReadsAsync()
    {
        var services = await ServicesAsync();
        var caddyfile = await File.ReadAllTextAsync(Path.Combine(Root, "deploy", "fleet", "Caddyfile"), Cancellation);
        var given = ((YamlMappingNode)((YamlMappingNode)services["caddy"])["environment"]).Children.Keys.Select(key => key.ToString()).ToHashSet(StringComparer.Ordinal);

        Assert.Subset(given, Placeholder().Matches(caddyfile).Select(match => match.Groups["name"].Value).ToHashSet(StringComparer.Ordinal));
    }

    [Fact]
    public async Task OnlyCaddyPublishesPortsAsync()
    {
        var services = await ServicesAsync();

        Assert.Equal(["caddy"], services.Children.Where(service => ((YamlMappingNode)service.Value).Children.ContainsKey("ports")).Select(service => service.Key.ToString()));
    }

    [Fact]
    public async Task TheFleetScriptUsesTheAdministratorKeyOnlyInAHeaderAsync()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(Root, "deploy", "fleet", "fleet"), Cancellation);
        var uses = KeyUse().Matches(script).Select(match => match.Value).ToList();

        Assert.All(uses, use => Assert.Matches(KeyInHeader(), use));
        Assert.DoesNotContain("echo \"$AVALA_KEY", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheDeploymentCheckNeverRunsAsTheComposeProjectOfARealFleetAsync()
    {
        var check = await File.ReadAllTextAsync(Path.Combine(Root, "scripts", "fleet-deploy-check.cs"), Cancellation);

        Assert.Equal(
            (true, true, true, 1),
            (check.Contains("Shell.Project = run;", StringComparison.Ordinal),
                check.Contains("info.Environment[\"COMPOSE_PROJECT_NAME\"] = Project;", StringComparison.Ordinal) && check.Contains("info.Environment[\"AVALA_FLEET_PROJECT\"] = Project;", StringComparison.Ordinal),
                check.Contains("Ports.Taken(8080, 8443)", StringComparison.Ordinal),
                ProcessStart().Count(check)));
    }

    private static async Task<YamlMappingNode> ServicesAsync()
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(await File.ReadAllTextAsync(Path.Combine(Root, "deploy", "fleet", "compose.yaml"), Cancellation)));

        return (YamlMappingNode)((YamlMappingNode)stream.Documents[0].RootNode)["services"];
    }

    private static string RootOf(string directory) =>
        File.Exists(Path.Combine(directory, "Avala.slnx")) ? directory : RootOf(Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar))!);

    [GeneratedRegex("<ContainerRepository>(?<name>[^<]+)</ContainerRepository>")]
    private static partial Regex Repository();

    [GeneratedRegex("Image = \"(?<image>[^\"]+)\"")]
    private static partial Regex Image();

    [GeneratedRegex(@"\{\$(?<name>[A-Z_]+)\}")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"Process\.Start\(")]
    private static partial Regex ProcessStart();

    [GeneratedRegex(@"[^\n]*\$AVALA_KEY[^\n]*")]
    private static partial Regex KeyUse();

    [GeneratedRegex("Authorization: Bearer \\$AVALA_KEY|AVALA_KEY=\\$\\(setting|export AVALA_KEY|\\[ -n \"\\$AVALA_KEY\" \\]")]
    private static partial Regex KeyInHeader();
}
