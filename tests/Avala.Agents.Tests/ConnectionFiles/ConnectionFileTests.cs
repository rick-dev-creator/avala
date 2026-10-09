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
        Assert.Equal(new ConnectionName("personal"), declared.Default);
    }

    [Fact]
    public void WithoutADefaultTheFirstDeclaredConnectionIsTheDefault()
    {
        var declared = Outcomes.Succeeds(ConnectionFileParser.Parse("""{ "connections": [ { "name": "b", "provider": "x" }, { "name": "a", "provider": "x" } ] }"""));

        Assert.Equal(new ConnectionName("b"), declared.Default);
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
    [InlineData("""{ "default": "b", "connections": [ { "name": "a", "provider": "x" } ] }""", ConnectionError.UnknownDefault)]
    [InlineData("{}", ConnectionError.NoConnections)]
    [InlineData("""{ "connections": [] }""", ConnectionError.NoConnections)]
    public void AnInvalidFileIsRejectedWithItsReason(string text, ConnectionError expected) =>
        Assert.Equal(expected, Outcomes.FailsWith(ConnectionFileParser.Parse(text)));

    [Fact]
    public void ASwitchSettingMayBeWrittenAsABooleanAndReachesTheProviderAsText()
    {
        var declared = Outcomes.Succeeds(ConnectionFileParser.Parse(
            """{ "connections": [ { "name": "a", "provider": "x", "settings": { "userHooks": true, "userConfiguration": false } } ] }"""));

        Assert.Equal(
            ["userConfiguration=false", "userHooks=true"],
            declared.Connections.Single().Settings.Select(setting => $"{setting.Key}={setting.Value}").Order(StringComparer.Ordinal));
    }

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
}
