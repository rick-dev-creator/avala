using Avala.Agents.ConnectionFiles;
using Avala.Agents.Contracts.Connections;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Agents.Tests.ConnectionFiles;

public sealed class ConnectionFileTests
{
    private const string Declared = """
        {
          "default": "personal",
          "connections": [
            { "name": "work", "provider": "simulator", "credential": { "source": "login", "reference": "/logins/work" }, "settings": { "model": "large" } },
            { "name": "personal", "provider": "simulator", "credential": { "source": "apiKey", "reference": "PERSONAL_KEY" } },
            { "name": "plain", "provider": "other" }
          ]
        }
        """;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public void AValidFileDeclaresEveryConnectionWithItsProviderCredentialSettingsAndTheDefault()
    {
        var declared = Outcomes.Succeeds(ConnectionFileParser.Parse(Declared));

        Assert.Equal(
            [
                ("work", "simulator", "login", "/logins/work", "model=large"),
                ("personal", "simulator", "apiKey", "PERSONAL_KEY", string.Empty),
                ("plain", "other", "-", "-", string.Empty),
            ],
            declared.Connections.Select(connection => (
                connection.Name.Value,
                connection.Provider,
                connection.Credential.Match(credential => credential.Source, () => "-"),
                connection.Credential.Bind(credential => credential.Reference).Match(reference => reference, () => "-"),
                string.Join(',', connection.Settings.Select(setting => $"{setting.Key}={setting.Value}")))));
        Assert.Equal((DefaultMode.Fixed, Option<ConnectionName>.Some(new ConnectionName("personal"))), (declared.Mode, declared.Default));
    }

    [Theory]
    [InlineData("""{ "connections": [ { "name": "b", "provider": "x" }, { "name": "a", "provider": "x" } ] }""")]
    [InlineData("""{ "default": "auto", "connections": [ { "name": "b", "provider": "x" }, { "name": "a", "provider": "x" } ] }""")]
    public void WithoutADefaultOrWithAutoTheMachineChoosesByCapacityAndFallsBackToTheFirstConnection(string text)
    {
        var declared = Outcomes.Succeeds(ConnectionFileParser.Parse(text));

        Assert.Equal((DefaultMode.Auto, Option<ConnectionName>.Some(new ConnectionName("b"))), (declared.Mode, declared.Default));
    }

    [Theory]
    [InlineData("{}", "auto")]
    [InlineData("""{ "default": "claude-work" }""", "claude-work")]
    public void AFileMayDeclareOnlyTheDefaultAndLeaveTheConnectionsToDiscovery(string text, string named)
    {
        var declared = Outcomes.Succeeds(ConnectionFileParser.Parse(text));

        Assert.Equal((0, named), (declared.Connections.Count, declared.Fixed.Match(name => name.Value, () => "auto")));
    }

    [Theory]
    [InlineData("not json", ConnectionError.Malformed)]
    [InlineData("[]", ConnectionError.Malformed)]
    [InlineData("""{ "connections": {} }""", ConnectionError.Malformed)]
    [InlineData("""{ "connections": [ "work" ] }""", ConnectionError.Malformed)]
    [InlineData("""{ "connections": [ { "name": 1, "provider": "x" } ] }""", ConnectionError.Malformed)]
    [InlineData("""{ "connections": [ { "name": "a", "name": "b", "provider": "x" } ] }""", ConnectionError.Malformed)]
    [InlineData("""{ "connections": [ { "name": "a", "provider": "x", "settings": { "model": 1 } } ] }""", ConnectionError.Malformed)]
    [InlineData("""{ "connections": [ { "name": "a", "provider": "x", "credential": "login" } ] }""", ConnectionError.Malformed)]
    [InlineData("""{ "connections": [ { "name": "a", "provider": "x", "settings": { "deep": { "er": "x" } } } ] }""", ConnectionError.Malformed)]
    [InlineData("""{ "connection": [] }""", ConnectionError.UnknownField)]
    [InlineData("""{ "connections": [ { "name": "a", "provider": "x", "apiKey": "sk-1" } ] }""", ConnectionError.UnknownField)]
    [InlineData("""{ "connections": [ { "name": "a", "provider": "x", "credential": { "source": "apiKey", "secret": "sk-1" } } ] }""", ConnectionError.UnknownField)]
    [InlineData("""{ "connections": [ { "provider": "x" } ] }""", ConnectionError.InvalidName)]
    [InlineData("""{ "connections": [ { "name": "my work", "provider": "x" } ] }""", ConnectionError.InvalidName)]
    [InlineData("""{ "connections": [ { "name": ".hidden", "provider": "x" } ] }""", ConnectionError.InvalidName)]
    [InlineData("""{ "connections": [ { "name": "a", "provider": "x" }, { "name": "a", "provider": "y" } ] }""", ConnectionError.DuplicateName)]
    [InlineData("""{ "connections": [ { "name": "a" } ] }""", ConnectionError.MissingProvider)]
    [InlineData("""{ "connections": [ { "name": "a", "provider": " " } ] }""", ConnectionError.MissingProvider)]
    [InlineData("""{ "connections": [ { "name": "a", "provider": "x", "credential": {} } ] }""", ConnectionError.MissingSource)]
    [InlineData("""{ "connections": [ { "name": "a", "provider": "x", "credential": { "source": "apiKey", "reference": " " } } ] }""", ConnectionError.MissingReference)]
    [InlineData("""{ "default": 1 }""", ConnectionError.Malformed)]
    [InlineData("""{ "default": "auto", "fallback": "work" }""", ConnectionError.UnknownField)]
    [InlineData("""{ "connections": [] }""", ConnectionError.NoConnections)]
    public void AnInvalidFileIsRejectedWithItsReason(string text, ConnectionError expected) =>
        Assert.Equal(expected, Outcomes.FailsWith(ConnectionFileParser.Parse(text)));

    [Fact]
    public async Task WithoutAConnectionsFileNothingIsDeclaredAsync()
    {
        using var data = new TemporaryFolder();

        Assert.True(Outcomes.Succeeds(await new ConnectionFileReader(new AvalaPaths(data.Path)).LoadAsync(Cancellation)).IsNone);
    }

    [Fact]
    public async Task TheConnectionsFileOfTheDataFolderIsReadAsync()
    {
        using var data = new TemporaryFolder();
        await File.WriteAllTextAsync(Path.Combine(data.Path, ConnectionFileReader.FileName), Declared, Cancellation);

        var declared = Outcomes.Present(Outcomes.Succeeds(await new ConnectionFileReader(new AvalaPaths(data.Path)).LoadAsync(Cancellation)));

        Assert.Equal(["work", "personal", "plain"], declared.Connections.Select(connection => connection.Name.Value));
    }

    [Fact]
    public async Task AConnectionsFileOverTheSizeLimitIsRejectedUnparsedAsync()
    {
        using var data = new TemporaryFolder();
        await File.WriteAllTextAsync(
            Path.Combine(data.Path, ConnectionFileReader.FileName),
            new string(' ', ConnectionFileReader.MaximumBytes + 1),
            Cancellation);

        Assert.Equal(ConnectionError.TooLarge, Outcomes.FailsWith(await new ConnectionFileReader(new AvalaPaths(data.Path)).LoadAsync(Cancellation)));
    }

    [Fact]
    public async Task ChangingTheDefaultRewritesOnlyItKeepsEveryConnectionAndIsReadBackAtOnceAsync()
    {
        using var data = new TemporaryFolder();
        await File.WriteAllTextAsync(Path.Combine(data.Path, ConnectionFileReader.FileName), Declared, Cancellation);
        await using var reader = new ConnectionFileReader(new AvalaPaths(data.Path));
        _ = await reader.LoadAsync(Cancellation);

        var changed = Outcomes.Succeeds(await reader.ChangeDefaultAsync(new ConnectionName("work"), Cancellation));
        var automatic = Outcomes.Succeeds(await reader.ChangeDefaultAsync(Option<ConnectionName>.None, Cancellation));

        Assert.Equal(DefaultMode.Fixed, changed.Mode);
        Assert.Equal((DefaultMode.Auto, 3), (automatic.Mode, automatic.Connections.Count));
        var reread = Outcomes.Present(Outcomes.Succeeds(await new ConnectionFileReader(new AvalaPaths(data.Path)).LoadAsync(Cancellation)));
        Assert.Equal(["work", "personal", "plain"], reread.Connections.Select(connection => connection.Name.Value));
        Assert.Equal((DefaultMode.Auto, "model=large"), (reread.Mode, string.Join(',', reread.Connections[0].Settings.Select(setting => $"{setting.Key}={setting.Value}"))));
        Assert.Equal(DefaultMode.Auto, Outcomes.Present(Outcomes.Succeeds(await reader.LoadAsync(Cancellation))).Mode);
        Assert.Contains("\"default\": \"auto\"", await File.ReadAllTextAsync(Path.Combine(data.Path, ConnectionFileReader.FileName), Cancellation), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChangingTheDefaultWithoutAFileCreatesOneThatHoldsOnlyTheDefaultAsync()
    {
        using var data = new TemporaryFolder();
        await using var reader = new ConnectionFileReader(new AvalaPaths(data.Path));

        var changed = Outcomes.Succeeds(await reader.ChangeDefaultAsync(new ConnectionName("claude-work"), Cancellation));

        Assert.Equal((0, Option<ConnectionName>.Some(new ConnectionName("claude-work"))), (changed.Connections.Count, changed.Fixed));
        Assert.Equal(
            Option<ConnectionName>.Some(new ConnectionName("claude-work")),
            Outcomes.Present(Outcomes.Succeeds(await new ConnectionFileReader(new AvalaPaths(data.Path)).LoadAsync(Cancellation))).Fixed);
    }

    [Fact]
    public async Task AFileThatCannotBeParsedIsNeverRewrittenAsync()
    {
        using var data = new TemporaryFolder();
        var path = Path.Combine(data.Path, ConnectionFileReader.FileName);
        await File.WriteAllTextAsync(path, """{ "connection": [] }""", Cancellation);
        await using var reader = new ConnectionFileReader(new AvalaPaths(data.Path));

        var refused = await reader.ChangeDefaultAsync(Option<ConnectionName>.None, Cancellation);

        Assert.Equal(ConnectionError.UnknownField, Outcomes.FailsWith(refused));
        Assert.Equal("""{ "connection": [] }""", await File.ReadAllTextAsync(path, Cancellation));
    }

    [Fact]
    public async Task AFolderThatCannotBeWrittenRefusesTheChangeAsUnwritableAsync()
    {
        using var data = new TemporaryFolder();
        var blocked = Path.Combine(data.Path, "taken");
        await File.WriteAllTextAsync(blocked, string.Empty, Cancellation);
        await using var reader = new ConnectionFileReader(new AvalaPaths(Path.Combine(blocked, "data")));

        Assert.Equal(ConnectionError.Unwritable, Outcomes.FailsWith(await reader.ChangeDefaultAsync(Option<ConnectionName>.None, Cancellation)));
    }
}
