using Avala.Agents.Contracts.Connections;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Testing;
using Avala.Workbench.Navigation;
using Avala.Workbench.Settings;
using CommunityToolkit.Mvvm.Messaging;

namespace Avala.Workbench.Tests.Navigation;

public sealed class FirstRunViewModelScripts
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WithNoConnectionAtAllItSaysSoAndGuides()
    {
        using var bench = new Bench();
        var firstRun = FirstRun(bench, Catalog(ConnectionFileStatus.Absent));

        await firstRun.CheckAsync(Cancellation);

        Assert.Equal(
            (true, "No connections yet", "A job runs on a harness, and Avala found none to run it on: no Claude Code login on this computer and no connection declared in connections.json."),
            (firstRun.IsShown, firstRun.Heading, firstRun.Explanation));
    }

    [Fact]
    public async Task WithOnlyImplicitConnectionsItSaysNoLoginWasFoundAndNamesThem()
    {
        using var bench = new Bench();
        var firstRun = FirstRun(bench, Catalog(ConnectionFileStatus.Absent, Connection("claude-code", ConnectionOrigin.Implicit)));

        await firstRun.CheckAsync(Cancellation);

        Assert.Equal((true, "No login found"), (firstRun.IsShown, firstRun.Heading));
        Assert.Contains("so jobs run on claude-code, as the harness itself is set up", firstRun.Explanation, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(nameof(ConnectionOrigin.Declared))]
    [InlineData(nameof(ConnectionOrigin.Discovered))]
    public async Task ADeclaredOrDiscoveredConnectionHidesTheGuide(string origin)
    {
        using var bench = new Bench();
        var firstRun = FirstRun(bench, Catalog(ConnectionFileStatus.Applied, Connection("claude-code", ConnectionOrigin.Implicit), Connection("claude-work", Enum.Parse<ConnectionOrigin>(origin))));

        await firstRun.CheckAsync(Cancellation);

        Assert.Equal((false, string.Empty), (firstRun.IsShown, firstRun.Heading));
    }

    [Fact]
    public async Task ARejectedConnectionsFileIsNamedInsteadOfCalledAFirstRun()
    {
        using var bench = new Bench();
        var firstRun = FirstRun(bench, Catalog(ConnectionFileStatus.Rejected));

        await firstRun.CheckAsync(Cancellation);

        Assert.Equal((true, "connections.json is rejected"), (firstRun.IsShown, firstRun.Heading));
    }

    [Fact]
    public void OpenSettingsAsksTheShellForTheSettingsPage()
    {
        using var bench = new Bench();
        var settings = Settings();
        var requested = new List<IPage>();
        bench.Messenger.Register<PageRequested>(requested, (pages, message) => ((List<IPage>)pages).Add(message.Page));

        ViewModelScript.Given(new FirstRunViewModel(new FakeConnections(), new SettingsLink(settings, bench.Focus)))
            .Invoke("OpenSettingsCommand");

        Assert.Equal([settings], requested);
    }

    private static FirstRunViewModel FirstRun(Bench bench, ConnectionCatalog catalog) =>
        new(new FakeConnections { Catalog = catalog }, new SettingsLink(Settings(), bench.Focus));

    private static SettingsViewModel Settings() =>
        new(new DesignRepositorySettingsViewModel(), new DesignMachineSettingsViewModel(), new DesignAppearanceViewModel(), new DesignAboutViewModel());

    private static ConnectionCatalog Catalog(ConnectionFileStatus file, params DeclaredConnection[] connections) =>
        new(file, Option<ConnectionError>.None, connections, Option<ConnectionName>.None);

    private static DeclaredConnection Connection(string name, ConnectionOrigin origin) =>
        new(new ConnectionName(name), "claude-code", Option<string>.None) { Origin = origin };
}
