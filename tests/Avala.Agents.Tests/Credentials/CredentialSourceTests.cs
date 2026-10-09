using Avala.Agents.Contracts.Connections;
using Avala.Agents.Credentials;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Agents.Tests.Credentials;

public sealed class CredentialSourceTests
{
    private static readonly ConnectionName Work = new("work");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ALoginNamesTheConfigurationFolderItReferencesRelativeToTheDataFolderAsync()
    {
        using var data = new TemporaryFolder();
        var folder = Directory.CreateDirectory(Path.Combine(data.Path, "logins", "work")).FullName;
        var source = new LoginFolderSource(new AvalaPaths(data.Path));

        var absolute = Outcomes.Succeeds(await source.ResolveAsync(new CredentialRequest(Work, folder), Cancellation));
        var relative = Outcomes.Succeeds(await source.ResolveAsync(new CredentialRequest(Work, Path.Combine("logins", "work")), Cancellation));

        Assert.Equal([Option<string>.Some(folder), Option<string>.Some(folder)], [absolute.ConfigurationDirectory, relative.ConfigurationDirectory]);
        Assert.True(absolute.ApiKey.IsNone);
    }

    [Fact]
    public async Task ALoginWithoutAReferenceKeepsItsConfigurationInTheConnectionsOwnFolderAsync()
    {
        using var data = new TemporaryFolder();
        var folder = Directory.CreateDirectory(Path.Combine(data.Path, LoginFolderSource.FolderName, "work")).FullName;

        var resolved = Outcomes.Succeeds(await new LoginFolderSource(new AvalaPaths(data.Path)).ResolveAsync(new CredentialRequest(Work, Option<string>.None), Cancellation));

        Assert.Equal(Option<string>.Some(folder), resolved.ConfigurationDirectory);
    }

    [Fact]
    public async Task ALoginWhoseFolderIsMissingIsRejectedAsync()
    {
        using var data = new TemporaryFolder();

        var resolved = await new LoginFolderSource(new AvalaPaths(data.Path)).ResolveAsync(new CredentialRequest(Work, Option<string>.None), Cancellation);

        Assert.Equal(ConnectionError.MissingFolder, Outcomes.FailsWith(resolved));
    }

    [Fact]
    public async Task AnApiKeyIsReadFromTheVariableItReferencesAndNeverPrintedAsync()
    {
        var variable = $"AVALA_TEST_KEY_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(variable, "sk-test-123");

        try
        {
            var resolved = Outcomes.Succeeds(await new ApiKeySource().ResolveAsync(new CredentialRequest(Work, variable), Cancellation));

            Assert.Equal(Option<Secret>.Some(new Secret("sk-test-123")), resolved.ApiKey);
            Assert.DoesNotContain("sk-test-123", resolved.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public async Task AnApiKeyWhoseVariableIsUnsetOrUnnamedIsRejectedAsync()
    {
        var source = new ApiKeySource();

        var unset = await source.ResolveAsync(new CredentialRequest(Work, $"AVALA_TEST_KEY_{Guid.NewGuid():N}"), Cancellation);
        var unnamed = await source.ResolveAsync(new CredentialRequest(Work, Option<string>.None), Cancellation);

        Assert.Equal([ConnectionError.MissingVariable, ConnectionError.MissingReference], [Outcomes.FailsWith(unset), Outcomes.FailsWith(unnamed)]);
    }
}
