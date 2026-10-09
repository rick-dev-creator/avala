using Avala.Sdk;
using Avala.Testing;
using Avala.Workbench.Machine;
using Avala.Workbench.Settings;
using CommunityToolkit.Mvvm.Messaging;

namespace Avala.Workbench.Tests.Settings;

public sealed class MachineSettingsViewModelScripts
{
    private readonly FakeOpener opener = new();
    private readonly FakeSupervision supervision = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheMachineSettingsListTheConnectionsAndOpenTheirFileAsync()
    {
        var machine = Machine();
        await machine.LoadAsync(Cancellation);

        await machine.OpenConnectionsCommand.ExecuteAsync(null);

        Assert.Equal([("work", true, "login"), ("personal", false, "login")], machine.Connections.Select(connection => (connection.Name, connection.IsDefault, connection.Source)));
        Assert.Equal([Path.Combine("/data", "connections.json")], opener.Opened);
        Assert.Equal(("Applied", "900s", "Absent", "900"), (machine.ConnectionsFile, machine.Silence, machine.SupervisionFile, machine.SilenceDraft));
    }

    [Fact]
    public async Task AConnectionsFileThePlatformCannotOpenSaysWhy()
    {
        var machine = Machine();
        opener.Refusal = FileOpenError.Unavailable;

        await machine.OpenConnectionsCommand.ExecuteAsync(null);

        Assert.Equal("No application is available to open the file.", machine.Error);
    }

    [Fact]
    public async Task AValidSilenceWindowIsSavedAndShownAsync()
    {
        var machine = Machine();
        machine.SilenceDraft = " 90 ";

        await machine.SaveSilenceCommand.ExecuteAsync(null);

        Assert.Equal([TimeSpan.FromSeconds(90)], supervision.Changes);
        Assert.Equal(("90s", "Applied", string.Empty), (machine.Silence, machine.SupervisionFile, machine.Error));
    }

    [Theory]
    [InlineData("soon", "Enter the window in seconds.", 0)]
    [InlineData("NaN", "Enter the window in seconds.", 0)]
    [InlineData("0", "The window must be more than 0 and at most 86,400 seconds.", 1)]
    public async Task AnInvalidSilenceWindowIsRefusedWithItsReasonAsync(string draft, string error, int changes)
    {
        var machine = Machine();
        machine.SilenceDraft = draft;

        await machine.SaveSilenceCommand.ExecuteAsync(null);

        Assert.Equal((error, changes), (machine.Error, supervision.Changes.Count));
    }

    [Fact]
    public async Task EditingTheWindowMarksItChangedUntilItIsSavedAsync()
    {
        var machine = Machine();
        await machine.LoadAsync(Cancellation);
        var loaded = machine.IsSilenceChanged;

        machine.SilenceDraft = "120";
        var edited = machine.IsSilenceChanged;
        await machine.SaveSilenceCommand.ExecuteAsync(null);

        Assert.Equal((false, true, false), (loaded, edited, machine.IsSilenceChanged));
    }

    [Fact]
    public void AnEmptyDraftCannotBeSaved() =>
        ViewModelScript.Given(Machine())
            .When(machine => machine.SilenceDraft = " ")
            .Then(machine => Assert.False(machine.SaveSilenceCommand.CanExecute(null)));

    [Fact]
    public async Task AChangedDefaultMovesTheDefaultTagToItsConnectionAsync()
    {
        var connections = new FakeConnections("work", "personal");
        var machine = Machine(connections);
        await machine.LoadAsync(Cancellation);

        machine.DefaultConnection.Draft = "personal";
        await machine.DefaultConnection.SaveCommand.ExecuteAsync(null);

        Assert.Equal([("work", false), ("personal", true)], machine.Connections.Select(connection => (connection.Name, connection.IsDefault)));
    }

    [Fact]
    public async Task UnderAutoNoConnectionIsTaggedAsTheDefaultAsync()
    {
        var machine = Machine(new FakeConnections("work", "personal").Automatic());

        await machine.LoadAsync(Cancellation);

        Assert.All(machine.Connections, connection => Assert.False(connection.IsDefault));
        Assert.Equal(DefaultPhrases.Auto, machine.DefaultConnection.Saved);
    }

    private MachineSettingsViewModel Machine() => Machine(new FakeConnections("work", "personal"));

    private MachineSettingsViewModel Machine(FakeConnections connections)
    {
        var settings = new MachineSettings(connections, supervision, new FakeResources());

        return new(settings, new SettingsFiles(opener, new AvalaPaths("/data")), new DefaultConnectionViewModel(settings, new StrongReferenceMessenger()));
    }
}
