using Avala.Forges.Accounts;
using Avala.Forges.Connecting;
using Avala.Forges.Contracts;
using Avala.Forges.Detection;
using Avala.Forges.Policy;
using Avala.Forges.Transport;
using Avala.Sdk;
using Avala.Sdk.Processes;

namespace Avala.Forges.Tests.Accounts;

public sealed class SourceControlAccountsTests : IDisposable
{
    private static readonly Uri Hub = new("https://api.hub.example");

    private static readonly CliCall HubStatus = new("hub", ["auth", "status"], CliStatusChannel.Output);

    private static readonly CliCall CupLogins = new("cup", ["login", "list"], CliStatusChannel.Output);

    private readonly ForgeHttp http = new();
    private readonly Runner runner = new();
    private readonly Probes probes = new();
    private readonly Dictionary<string, string> environment = new(StringComparer.Ordinal);
    private ForgeSettings declared = ForgeSettings.None;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ACliLoginIsDetectedWithItsAccountAndTheCliIsInstalledAsync()
    {
        runner.Answers["hub"] = new ProcessOutcome(0, "octocat", string.Empty);

        var found = await Accounts(new FakeLogins("hub", HubStatus, [new DetectedLogin(Hub, "octocat")])).AccountsAsync(Cancellation);

        var account = Assert.Single(found.Accounts);
        Assert.Equal(
            (new ForgeName("hub"), "hub", "api.hub.example", CredentialSource.Cli, Option<ForgeAccount>.Some(new ForgeAccount("octocat")), AccountOrigin.Detected),
            (account.Name, account.ForgeKind, account.Host, account.Source, account.Account, account.Origin));
        var tool = Assert.Single(found.Tools);
        Assert.Equal(("hub", "hub", Option<string>.Some("hub auth login"), true), (tool.Command, Assert.Single(tool.Forges), tool.SignIn, tool.Installed));
    }

    [Fact]
    public async Task CliLoginsOfOneToolAreToldApartByTheForgesProbeAsync()
    {
        runner.Answers["cup"] = new ProcessOutcome(0, "[]", string.Empty);
        probes.Responding.Add("https://codeberg.org/api/saucer/v1/version");
        var logins = new FakeLogins("cup", CupLogins, [new DetectedLogin(new Uri("https://codeberg.org"), "ada") { Reference = "codeberg" }, new DetectedLogin(new Uri("https://cup.example.com"), "ada") { Reference = "work" }])
        {
            Probe = new ForgeProbe("/api/saucer/v1/version", "saucer"),
        };

        var found = await Accounts(logins).AccountsAsync(Cancellation);

        Assert.Equal([("saucer", "codeberg.org", "codeberg"), ("cup", "cup.example.com", "work")], found.Accounts.Select(account => (account.ForgeKind, account.Host, account.Reference.Match(reference => reference, () => string.Empty))));
        Assert.Equal(["cup", "saucer"], Assert.Single(found.Tools).Forges);
    }

    [Fact]
    public async Task ATokenVariableThatIsSetIsDetectedByItsNameOnlyAsync()
    {
        environment["HUB_TOKEN"] = "secret-value";
        environment["OTHER_TOKEN"] = string.Empty;

        var found = await Accounts(new FakeLogins("hub", Option<CliCall>.None, []) { TokenVariables = ["HUB_TOKEN", "OTHER_TOKEN"] }).AccountsAsync(Cancellation);

        var account = Assert.Single(found.Accounts);
        Assert.Equal((CredentialSource.Environment, Option<string>.Some("HUB_TOKEN"), "api.hub.example"), (account.Source, account.Reference, account.Host));
        Assert.DoesNotContain("secret-value", account.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAccountAlreadyDeclaredIsLeftOutOfTheDetectedOnesAndASuggestedNameIsNeverTakenAsync()
    {
        runner.Answers["hub"] = new ProcessOutcome(0, string.Empty, string.Empty);
        environment["HUB_TOKEN"] = "secret-value";
        declared = new ForgeSettings([new ForgeDeclaration(new ForgeName("hub"), "hub", Option<Uri>.None, CredentialSource.Cli, Option<string>.None)], ForgeSettings.DefaultPoll);

        var found = await Accounts(new FakeLogins("hub", HubStatus, [new DetectedLogin(Hub, "octocat")]) { TokenVariables = ["HUB_TOKEN"] }).AccountsAsync(Cancellation);

        Assert.Equal(
            [("hub", AccountOrigin.Declared, CredentialSource.Cli), ("hub-2", AccountOrigin.Detected, CredentialSource.Environment)],
            found.Accounts.Select(account => (account.Name.Value, account.Origin, account.Source)));
    }

    [Fact]
    public async Task ACliThatIsNotInstalledIsSaidSoAndNamesNoLoginAsync()
    {
        var found = await Accounts(new FakeLogins("hub", HubStatus, [new DetectedLogin(Hub, "octocat")])).AccountsAsync(Cancellation);

        Assert.Empty(found.Accounts);
        Assert.False(Assert.Single(found.Tools).Installed);
    }

    public void Dispose() => http.Dispose();

    private SourceControlDesk Accounts(FakeLogins logins)
    {
        IForge[] kinds = [new KindOnly("hub", Hub), new KindOnly("cup", Option<Uri>.None), new KindOnly("saucer", Option<Uri>.None)];
        var file = new Declared(() => declared);
        var connections = new ForgeConnections(file, kinds, new ForgeTransports(runner, http) { Environment = Lookup });

        return new SourceControlDesk(connections, kinds, new LoginDetection([logins], kinds, runner, probes) { Environment = Lookup });
    }

    private Option<string> Lookup(string variable) => environment.TryGetValue(variable, out var value) ? value : Option<string>.None;

    private sealed class FakeLogins(string forge, Option<CliCall> logins, IReadOnlyList<DetectedLogin> found) : IForgeLogins
    {
        public string Forge => forge;

        public Option<CliCall> Logins => logins;

        public Option<string> SignInCommand => $"{forge} auth login";

        public IReadOnlyList<string> TokenVariables { get; init; } = [];

        public Option<ForgeProbe> Probe { get; init; }

        public IReadOnlyList<DetectedLogin> Read(ProcessOutcome outcome) => outcome.Succeeded ? found : [];
    }

    private sealed class Declared(Func<ForgeSettings> settings) : IForgeFile
    {
        public ValueTask<Result<ForgeSettings, ForgeError>> ReadAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<ForgeSettings, ForgeError>.Success(settings()));
    }

    private sealed class Runner : IProcessRunner
    {
        public Dictionary<string, ProcessOutcome> Answers { get; } = new(StringComparer.Ordinal);

        public ValueTask<Result<ProcessOutcome, ProcessError>> RunAsync(ProcessRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Answers.TryGetValue(request.FileName, out var outcome) ? Result<ProcessOutcome, ProcessError>.Success(outcome) : ProcessError.NotFound);
    }

    private sealed class Probes : IForgeProbes
    {
        public HashSet<string> Responding { get; } = new(StringComparer.Ordinal);

        public ValueTask<bool> RespondsAsync(Uri address, CancellationToken cancellationToken) => ValueTask.FromResult(Responding.Contains(address.AbsoluteUri));
    }
}
