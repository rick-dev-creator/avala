using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

if (!OperatingSystem.IsLinux())
{
    await Console.Error.WriteLineAsync("The deployment check runs on Linux, where .NET reads SSL_CERT_FILE to trust the compose's own certificate authority.");

    return 2;
}

if (Ports.Taken(8080, 8443) is { Count: > 0 } taken)
{
    await Console.Error.WriteLineAsync($"Port {string.Join(" and ", taken)} is in use. The check maps 8080 and 8443; stop what listens there first.");

    return 2;
}

var run = $"avala-fleet-check-{RandomNumberGenerator.GetHexString(8, lowercase: true)}";
Shell.Project = run;
var deployment = new Deployment(Repository.Root(), Path.Combine(Path.GetTempPath(), run), args.Length > 1 && args[0] == "--previous" ? args[1] : "15a23ccf");

return await deployment.CheckAsync();

[System.Runtime.Versioning.SupportedOSPlatform("linux")]
internal sealed class Deployment(string root, string work, string previousCommit)
{
    private const string Server = "https://localhost:8443";

    private const string Checked = "remote:github.com/rick-dev-creator/fleet-check";

    private static readonly string[] Files = ["compose.yaml", "Caddyfile", "fleet"];

    private static readonly string[] Tags = ["check-new", "check-previous"];

    private static readonly string[] Bundles = ["/etc/ssl/certs/ca-certificates.crt", "/etc/pki/tls/certs/ca-bundle.crt", "/etc/ssl/cert.pem"];

    private readonly string deploy = Path.Combine(work, "deploy");

    private readonly Check check = new(Path.Combine(work, "deploy"));

    private string Fleet => Path.Combine(deploy, "fleet");

    public async Task<int> CheckAsync()
    {
        try
        {
            Directory.CreateDirectory(deploy);
            check.Say($"Working in {work}, as the compose project {Shell.Project}");
            var (reference, administratorKey) = await PrepareAsync();
            var (key, trust) = await UpgradeAsync(reference, administratorKey);
            using var http = Trusted.Client(trust);
            var withScenario = await DesktopAsync(http, key, trust);
            await RestartsAsync(http, key, withScenario);
            await BackupAndRestoreAsync(key, withScenario);
            var logs = (await Shell.RunAsync("docker", ["compose", "logs", "--no-color"], deploy)).Output;
            check.Expect("No key appears in any container's log", check.Secrets.All(secret => !logs.Contains(secret, StringComparison.Ordinal)) && !Keys.Pattern().IsMatch(logs));
        }
        catch (Exception failure)
        {
            check.Fail(failure.Message);
        }
        finally
        {
            await CleanUpAsync();
        }

        return check.Report();
    }

    private async Task<(string Reference, string AdministratorKey)> PrepareAsync()
    {
        await check.StepAsync("Build the solution", () => Shell.RunAsync("dotnet", ["build", "Avala.slnx", "-v", "quiet"], root));
        var registry = (await Shell.RunAsync("docker", ["--version"], root)).Output.Contains("podman", StringComparison.OrdinalIgnoreCase) ? "Podman" : "Docker";
        var reference = Images.Reference(registry);
        await check.StepAsync("Build the image of the working tree", () => Images.PublishAsync(root, "check-new", registry));
        await check.StepAsync($"Build the image of {previousCommit}", () => PreviousImageAsync(registry));
        var image = (await Shell.RunAsync("docker", ["image", "inspect", $"{reference}:check-new", "--format", "{{.Config.User}} {{.Config.ExposedPorts}} {{.Size}}"], root)).Output.Trim();
        check.Say($"Image: user {image}");

        foreach (var file in Files)
        {
            File.Copy(Path.Combine(root, "deploy", "fleet", file), Path.Combine(deploy, file));
        }

        File.SetUnixFileMode(Fleet, (UnixFileMode)0b111_101_101);
        var administratorKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)).Replace('/', 'a').Replace('+', 'b');
        check.Secrets.Add(administratorKey);

        return (reference, administratorKey);
    }

    private async Task PreviousImageAsync(string registry)
    {
        var previous = Path.Combine(work, "previous");
        await Shell.RunAsync("git", ["worktree", "add", "--detach", previous, previousCommit], root);

        try
        {
            await Images.PublishAsync(previous, "check-previous", registry);
        }
        finally
        {
            await Shell.RunAsync("git", ["worktree", "remove", "--force", previous], root, allowFailure: true);
        }
    }

    private async Task<(string Key, X509Certificate2 Trust)> UpgradeAsync(string reference, string administratorKey)
    {
        await Settings.WriteAsync(deploy, reference, "check-previous", administratorKey);
        await check.StepAsync("Start the previous image", () => Compose.RunAsync(deploy, "up", "-d"));
        var trust = await check.HealthyAsync(Server);
        var key = await check.RegisterAsync("check-desktop");
        using var http = Trusted.Client(trust);
        await check.StepAsync("Send a job through the previous image", () => Wire.SendJobAsync(http, Server, key, Checked));
        var before = await Wire.TokensAsync(http, Server, key, Checked);
        check.Say($"The previous image holds {before} input tokens for {Checked}");

        await Settings.WriteAsync(deploy, reference, "check-new", administratorKey);
        await check.StepAsync("Upgrade to the new image", () => Compose.RunAsync(deploy, "up", "-d"));
        await check.HealthyAsync(Server);
        check.Expect("After the upgrade the instance key still answers", await Wire.WhoAmIAsync(http, Server, key) == 200);
        check.Expect("After the upgrade the job is still there", await Wire.TokensAsync(http, Server, key, Checked) == before);

        return (key, trust);
    }

    private async Task<decimal> DesktopAsync(HttpClient http, string key, X509Certificate2 trust)
    {
        var bundle = Path.Combine(work, "bundle.crt");
        var system = Bundles.FirstOrDefault(File.Exists) ?? throw new InvalidOperationException("No system certificate bundle was found to extend with Caddy's root.");
        await File.WriteAllTextAsync(bundle, await File.ReadAllTextAsync(system) + "\n" + trust.ExportCertificatePem());
        var scenario = await Shell.RunAsync(
            "dotnet",
            ["test", "--project", "tests/Avala.Host.Tests", "--no-build", "--", "--filter-method", "Avala.Host.Tests.FleetDeploymentTests.ASimulatedJobReachesTheDeployedServerAndTheDashboardShowsItAsync"],
            root,
            allowFailure: true,
            environment: new Dictionary<string, string> { ["AVALA_FLEET_SERVER"] = Server, ["AVALA_FLEET_KEY"] = key, ["SSL_CERT_FILE"] = bundle });
        check.Expect(
            "The real desktop connected over HTTPS with validation on, delivered a job and read it on the dashboard",
            scenario.Output.Contains("succeeded: 1", StringComparison.Ordinal) && scenario.Output.Contains("skipped: 0", StringComparison.Ordinal),
            scenario.Output);

        return await Wire.TokensAsync(http, Server, key, Checked);
    }

    private async Task RestartsAsync(HttpClient http, string key, decimal tokens)
    {
        await check.StepAsync("Restart the server", () => Compose.RunAsync(deploy, "restart", "server"));
        await check.HealthyAsync(Server);
        check.Expect("After a restart the key still answers", await Wire.WhoAmIAsync(http, Server, key) == 200);
        await check.StepAsync("Stop and start everything", async () =>
        {
            await Compose.RunAsync(deploy, "down");
            await Compose.RunAsync(deploy, "up", "-d");
        });
        await check.HealthyAsync(Server);
        check.Expect("After down and up the data is still there", await Wire.TokensAsync(http, Server, key, Checked) == tokens);
    }

    private async Task BackupAndRestoreAsync(string key, decimal tokens)
    {
        var backup = Path.Combine(work, "fleet.backup");
        await check.StepAsync("Back up", () => Shell.RunAsync(Fleet, ["backup", backup], deploy));
        await check.StepAsync("Delete every volume and start again", async () =>
        {
            await Compose.RunAsync(deploy, "down", "-v");
            await Compose.RunAsync(deploy, "up", "-d");
        });
        var trust = await check.HealthyAsync(Server);
        using var fresh = Trusted.Client(trust);
        check.Expect("With the volumes deleted the key is unknown", await Wire.WhoAmIAsync(fresh, Server, key) == 401);
        await check.StepAsync("Restore", () => Shell.RunAsync(Fleet, ["restore", backup], deploy));
        await check.HealthyAsync(Server);
        check.Expect("After the restore the key answers again", await Wire.WhoAmIAsync(fresh, Server, key) == 200);
        check.Expect("After the restore the data is back", await Wire.TokensAsync(fresh, Server, key, Checked) == tokens);
    }

    private async Task CleanUpAsync()
    {
        await Compose.RunAsync(deploy, "down", "-v", allowFailure: true);

        foreach (var built in Tags.SelectMany(tag => new[] { $"localhost/avala-server:{tag}", $"avala-server:{tag}" }))
        {
            await Shell.RunAsync("docker", ["image", "rm", built], root, allowFailure: true);
        }

        if (Directory.Exists(work))
        {
            Directory.Delete(work, recursive: true);
        }

        check.Say("Removed the containers, volumes, images and files the check made.");
    }
}

internal sealed class Check(string deploy)
{
    private readonly List<string> failures = [];

    public List<string> Secrets { get; } = [];

    public void Say(string line) => Console.WriteLine(Mask(line));

    public async Task StepAsync(string name, Func<Task> step)
    {
        Say($"- {name}");
        await step();
    }

    public void Expect(string claim, bool holds, string detail = "")
    {
        Say($"{(holds ? "PASS" : "FAIL")} {claim}");

        if (!holds)
        {
            failures.Add(claim);
            Say(detail);
        }
    }

    public void Fail(string reason)
    {
        failures.Add(reason);
        Say($"FAIL {reason}");
    }

    public async Task<X509Certificate2> HealthyAsync(string server)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            if (await HealthAsync(server) is { } trust)
            {
                Say($"PASS {server}/health answers 200 over HTTPS, trusting only Caddy's root");

                return trust;
            }

            await Pause.ForAsync(TimeSpan.FromSeconds(2));
        }

        throw new InvalidOperationException($"{server}/health never answered 200");
    }

    private async Task<X509Certificate2?> HealthAsync(string server)
    {
        var pem = await Shell.RunAsync(Path.Combine(deploy, "fleet"), ["root-certificate"], deploy, allowFailure: true);

        if (!pem.Succeeded || !pem.Output.Contains("BEGIN CERTIFICATE", StringComparison.Ordinal))
        {
            return null;
        }

        var trust = X509Certificate2.CreateFromPem(pem.Output);
        using var http = Trusted.Client(trust);

        try
        {
            using var response = await http.GetAsync(new Uri($"{server}/health"));

            return response.IsSuccessStatusCode ? trust : null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<string> RegisterAsync(string name)
    {
        var registered = await Shell.RunAsync(Path.Combine(deploy, "fleet"), ["register", name], deploy);
        var key = Keys.Pattern().Match(registered.Output).Value;
        Secrets.Add(key);
        Say(registered.Output.TrimEnd());
        Expect("./fleet register printed one instance key, once", Keys.Pattern().Count(registered.Output) == 1);

        return key;
    }

    public int Report()
    {
        Say(failures.Count == 0 ? "The deployment check passed." : $"The deployment check failed: {string.Join("; ", failures)}");

        return failures.Count == 0 ? 0 : 1;
    }

    private string Mask(string text) =>
        Keys.Pattern().Replace(Secrets.Aggregate(text, (masked, secret) => masked.Replace(secret, "***", StringComparison.Ordinal)), "avk_…");
}

internal static partial class Keys
{
    [GeneratedRegex("avk_[A-Za-z0-9_-]{43}")]
    public static partial Regex Pattern();
}

internal static class Pause
{
    public static Task ForAsync(TimeSpan wait)
    {
        var elapsed = new TaskCompletionSource();
        var timer = TimeProvider.System.CreateTimer(_ => elapsed.TrySetResult(), null, wait, Timeout.InfiniteTimeSpan);

        return elapsed.Task.ContinueWith(_ => timer.Dispose(), TaskScheduler.Default);
    }
}

[System.Runtime.Versioning.SupportedOSPlatform("linux")]
internal static class Settings
{
    public static async Task WriteAsync(string deploy, string image, string version, string administratorKey)
    {
        var path = Path.Combine(deploy, ".env");
        var password = File.Exists(path)
            ? (await File.ReadAllLinesAsync(path)).Single(line => line.StartsWith("AVALA_DATABASE_PASSWORD=", StringComparison.Ordinal))["AVALA_DATABASE_PASSWORD=".Length..]
            : RandomNumberGenerator.GetHexString(32, lowercase: true);
        await File.WriteAllLinesAsync(
            path,
            [
                $"AVALA_SERVER_IMAGE={image}",
                $"AVALA_SERVER_VERSION={version}",
                "AVALA_FLEET_DOMAIN=localhost",
                "AVALA_FLEET_TLS=internal",
                $"AVALA_ADMINISTRATOR_KEY={administratorKey}",
                $"AVALA_DATABASE_PASSWORD={password}",
                "AVALA_HTTP_PORT=8080",
                "AVALA_HTTPS_PORT=8443",
            ]);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}

internal static class Ports
{
    public static IReadOnlyList<int> Taken(params int[] ports) =>
        [
            .. ports.Where(port =>
            {
                try
                {
                    var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Any, port);
                    listener.Start();
                    listener.Stop();

                    return false;
                }
                catch (System.Net.Sockets.SocketException)
                {
                    return true;
                }
            }),
        ];
}

internal static class Images
{
    public static string Reference(string registry) => registry == "Podman" ? "localhost/avala-server" : "avala-server";

    public static Task PublishAsync(string tree, string tag, string registry) =>
        Shell.RunAsync(
            "dotnet",
            [
                "publish", "backend/Fleet/Avala.Server", "-t:PublishContainer", "-v", "quiet",
                "-p:RuntimeIdentifier=linux-x64", "-p:ContainerRuntimeIdentifiers=linux-x64",
                "-p:ContainerRepository=avala-server", "-p:ContainerFamily=noble-chiseled-extra",
                $"-p:ContainerImageTag={tag}", $"-p:LocalRegistry={registry}",
            ],
            tree);
}

internal static class Compose
{
    public static Task RunAsync(string deploy, params string[] arguments) => RunAsync(deploy, arguments, allowFailure: false);

    public static Task RunAsync(string deploy, string first, string second, bool allowFailure) => RunAsync(deploy, [first, second], allowFailure);

    private static async Task RunAsync(string deploy, string[] arguments, bool allowFailure)
    {
        if (Directory.Exists(deploy) && File.Exists(Path.Combine(deploy, ".env")))
        {
            await Shell.RunAsync("docker", ["compose", .. arguments], deploy, allowFailure);
        }
    }
}

internal static class Trusted
{
    public static HttpClient Client(X509Certificate2 root) =>
        new(new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, certificate, sent, errors) =>
                {
                    if (certificate is null || (errors & ~SslPolicyErrors.RemoteCertificateChainErrors) != SslPolicyErrors.None)
                    {
                        return false;
                    }

                    using var chain = new X509Chain();
                    chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                    chain.ChainPolicy.CustomTrustStore.Add(root);
                    chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

                    if (sent is not null)
                    {
                        chain.ChainPolicy.ExtraStore.AddRange(sent.ChainElements.Select(element => element.Certificate).ToArray());
                    }

                    return chain.Build(X509CertificateLoader.LoadCertificate(certificate.GetRawCertData()));
                },
            },
        })
        { Timeout = TimeSpan.FromSeconds(15) };
}

internal static class Wire
{
    public static async Task SendJobAsync(HttpClient http, string server, string key, string repository)
    {
        var job = Guid.NewGuid();
        var at = DateTimeOffset.UtcNow.AddMinutes(-5).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var body = $$"""
            {"events":[
              {"kind":"jobSubmitted","id":"{{Guid.NewGuid()}}","occurredAt":"{{at}}","job":"{{job}}","repository":"{{repository}}","model":"opus"},
              {"kind":"usageReported","id":"{{Guid.NewGuid()}}","occurredAt":"{{at}}","job":"{{job}}","inputTokens":1000,"outputTokens":100,"cacheReadTokens":0,"cacheWriteTokens":0,"reasoningTokens":0,"cost":{"amount":0.5,"currency":"USD","basis":"apiEquivalent"},"model":"opus"}]}
            """;
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"{server}/activity/v1/events")) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        using var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    public static async Task<int> WhoAmIAsync(HttpClient http, string server, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"{server}/instances/me"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        using var response = await http.SendAsync(request);

        return (int)response.StatusCode;
    }

    public static async Task<decimal> TokensAsync(HttpClient http, string server, string key, string repository)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var query = $"from={today.AddDays(-2):yyyy-MM-dd}&to={today.AddDays(2):yyyy-MM-dd}&zone=Europe%2FMadrid&step=day&repository={Uri.EscapeDataString(repository)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"{server}/activity/v1/usage?{query}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        using var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using var usage = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return usage.RootElement.GetProperty("totals").GetProperty("tokens").GetProperty("input").GetDecimal();
    }
}

internal sealed record ShellResult(bool Succeeded, string Output);

internal static class Shell
{
    public static string Project { get; set; } = string.Empty;

    public static async Task<ShellResult> RunAsync(string file, IReadOnlyList<string> arguments, string directory, bool allowFailure = false, IReadOnlyDictionary<string, string>? environment = null)
    {
        var info = new ProcessStartInfo(file) { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true };

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        if (Project.Length > 0)
        {
            info.Environment["COMPOSE_PROJECT_NAME"] = Project;
            info.Environment["AVALA_FLEET_PROJECT"] = Project;
        }

        foreach (var (name, value) in environment ?? new Dictionary<string, string>())
        {
            info.Environment[name] = value;
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException($"{file} could not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var text = await output + await error;

        if (process.ExitCode != 0 && !allowFailure)
        {
            throw new InvalidOperationException($"{Path.GetFileName(file)} {string.Join(' ', arguments.Take(3))} failed: {Keys.Pattern().Replace(text, "avk_…")}");
        }

        return new ShellResult(process.ExitCode == 0, text);
    }
}

internal static class Repository
{
    public static string Root()
    {
        for (var directory = new DirectoryInfo(Environment.CurrentDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Avala.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Run the check from inside the Avala repository.");
    }
}
