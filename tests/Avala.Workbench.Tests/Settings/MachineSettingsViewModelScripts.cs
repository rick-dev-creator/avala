using Avala.Agents.Contracts.Connections;
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

        Assert.Equal($"No application is available to open files: edit {Path.Combine("/data", "connections.json")} yourself.", machine.Error);
    }

    [Fact]
    public async Task AddingAConnectionWithoutAConnectionsFileCreatesItWithTheAutoDefaultThenOpensItAsync()
    {
        var connections = new FakeConnections("simulator").Automatic();
        connections.Catalog = connections.Catalog with { File = ConnectionFileStatus.Absent };
        var machine = Machine(connections);

        await machine.OpenConnectionsCommand.ExecuteAsync(null);

        Assert.Equal([Option<ConnectionName>.None], connections.Changes);
        Assert.Equal([Path.Combine("/data", "connections.json")], opener.Opened);
        Assert.Equal((string.Empty, SettingsPhrases.CreatedConnections, "Applied"), (machine.Error, machine.Notice, machine.ConnectionsFile));
    }

    [Fact]
    public async Task OpeningAnExistingConnectionsFileChangesNothingInItAsync()
    {
        var connections = new FakeConnections("work");
        var machine = Machine(connections);
        opener.Existing.Add(Path.Combine("/data", "connections.json"));

        await machine.OpenConnectionsCommand.ExecuteAsync(null);

        Assert.Empty(connections.Changes);
        Assert.Equal(string.Empty, machine.Notice);
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

        return new(settings, new SettingsFiles(opener, new AvalaPaths("/data")), new DefaultConnectionViewModel(settings, new StrongReferenceMessenger()), new ConnectionEditorViewModel(settings));
    }
}

public sealed class ConnectionEditorViewModelScripts
{
    private readonly FakeConnections connections = new("work", "personal");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ANewConnectionIsAddedToTheFileAndTheListAtOnceAsync()
    {
        var machine = await MachineAsync();
        machine.Editor.NewCommand.Execute(null);
        var blank = (machine.Editor.IsOpen, machine.Editor.Title, machine.Editor.Provider, machine.Editor.Source, machine.Editor.SaveCommand.CanExecute(null));

        machine.Editor.Name = "team";
        await machine.Editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal((true, "New connection", 0, 0, false), blank);
        Assert.Equal(["declare team simulator"], connections.Edits);
        Assert.Equal(["work", "personal", "team"], machine.Connections.Select(connection => connection.Name));
        Assert.Equal((false, "Added team to connections.json. New jobs can run on it now."), (machine.Editor.IsOpen, machine.Notice));
    }

    [Fact]
    public async Task AnApiKeyIsDeclaredByTheNameOfItsVariableNeverByTheKeyAsync()
    {
        var machine = await MachineAsync();
        machine.Editor.NewCommand.Execute(null);
        machine.Editor.Name = "team";
        machine.Editor.Source = 2;
        var withoutReference = machine.Editor.SaveCommand.CanExecute(null);

        machine.Editor.Reference = " TEAM_KEY ";
        await machine.Editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal((false, true), (withoutReference, machine.Editor.NeedsReference));
        Assert.Contains("never reads or writes the key", machine.Editor.ReferenceHint, StringComparison.Ordinal);
        Assert.Equal(["declare team simulator apiKey:TEAM_KEY"], connections.Edits);
    }

    [Fact]
    public async Task EditingAConnectionStartsFromWhatItDeclaresAndARenameSaysWhatElseMustChangeAsync()
    {
        connections.Catalog = connections.Catalog with
        {
            Connections = [new DeclaredConnection(new ConnectionName("work"), "claude-code", "login") { Reference = "/logins/work" }],
        };
        var machine = await MachineAsync();

        machine.Connections[0].EditCommand.Execute(null);
        var prefilled = (machine.Editor.Title, machine.Editor.Name, machine.Editor.Provider, machine.Editor.Source, machine.Editor.Reference);
        machine.Editor.Name = "office";
        await machine.Editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal(("Edit work", "work", 1, 1, "/logins/work"), prefilled);
        Assert.Equal(["declare work->office claude-code login:/logins/work"], connections.Edits);
        Assert.Equal("Renamed work to office in connections.json. A repository whose .avala/jobs.json names work must be changed too.", machine.Notice);
    }

    [Fact]
    public async Task RemovingAsksFirstAndKeepingChangesNothingAsync()
    {
        var machine = await MachineAsync();

        machine.Connections[1].RemoveCommand.Execute(null);
        var asked = machine.Editor.Removing;
        machine.Editor.KeepCommand.Execute(null);
        machine.Connections[1].RemoveCommand.Execute(null);
        await machine.Editor.RemoveCommand.ExecuteAsync(null);

        Assert.Equal("personal", asked);
        Assert.Equal(["remove personal"], connections.Edits);
        Assert.Equal(["work"], machine.Connections.Select(connection => connection.Name));
        Assert.Equal(string.Empty, machine.Editor.Removing);
    }

    [Theory]
    [InlineData(ConnectionError.RemovesTheDefault, "This is the default connection: choose another default first.")]
    [InlineData(ConnectionError.DuplicateName, "Another connection already has that name.")]
    public async Task ARefusedChangeSaysWhyAndKeepsTheConnectionsAsync(ConnectionError refusal, string reason)
    {
        var machine = await MachineAsync();
        connections.Refusal = refusal;

        machine.Connections[0].RemoveCommand.Execute(null);
        await machine.Editor.RemoveCommand.ExecuteAsync(null);

        Assert.Equal(reason, machine.Editor.Error);
        Assert.Equal(["work", "personal"], machine.Connections.Select(connection => connection.Name));
    }

    [Fact]
    public async Task TheFirstDeclaredConnectionSaysWhichImplicitConnectionsItReplacesAsync()
    {
        connections.Catalog = connections.Catalog with
        {
            Connections = [new DeclaredConnection(new ConnectionName("simulator"), "simulator", Option<string>.None) { Origin = ConnectionOrigin.Implicit }],
        };
        var machine = await MachineAsync();
        machine.Editor.NewCommand.Execute(null);
        machine.Editor.Name = "team";

        await machine.Editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal(
            "Added team to connections.json. New jobs can run on it now. A file that declares connections replaces the implicit ones, so simulator is no longer offered.",
            machine.Notice);
    }

    [Fact]
    public async Task OnlyAConnectionDeclaredInTheFileOffersEditAndRemoveAsync()
    {
        connections.Catalog = connections.Catalog with
        {
            Connections = [new DeclaredConnection(new ConnectionName("found"), "simulator", "login") { Origin = ConnectionOrigin.Discovered }],
        };

        var machine = await MachineAsync();

        Assert.False(machine.Connections[0].IsDeclared);
    }

    private async Task<MachineSettingsViewModel> MachineAsync()
    {
        var settings = new MachineSettings(connections, new FakeSupervision(), new FakeResources());
        var machine = new MachineSettingsViewModel(
            settings,
            new SettingsFiles(new FakeOpener(), new AvalaPaths("/data")),
            new DefaultConnectionViewModel(settings, new StrongReferenceMessenger()),
            new ConnectionEditorViewModel(settings));
        await machine.LoadAsync(Cancellation);

        return machine;
    }
}
