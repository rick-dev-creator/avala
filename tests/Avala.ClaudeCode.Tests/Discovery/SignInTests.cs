using Avala.ClaudeCode.Conversations;
using Avala.ClaudeCode.Discovery;
using Avala.Testing;

namespace Avala.ClaudeCode.Tests.Discovery;

public sealed class SignInTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WithoutALoginOrACredentialVariableClaudeCodeCannotRunAsync()
    {
        await using var home = new TemporaryFolder();

        Assert.False(new SignIn(new UserHome(home.Path, []), new Dictionary<string, string> { ["ANTHROPIC_API_KEY"] = string.Empty, ["PATH"] = "/bin" }).IsAvailable);
    }

    [Theory]
    [InlineData("ANTHROPIC_API_KEY")]
    [InlineData("ANTHROPIC_AUTH_TOKEN")]
    [InlineData("CLAUDE_CODE_OAUTH_TOKEN")]
    [InlineData("CLAUDE_CODE_USE_BEDROCK")]
    [InlineData("CLAUDE_CODE_USE_VERTEX")]
    public async Task ACredentialVariableLetsItRunWithoutALoginAsync(string variable)
    {
        await using var home = new TemporaryFolder();

        Assert.True(new SignIn(new UserHome(home.Path, []), new Dictionary<string, string> { [variable] = "set" }).IsAvailable);
    }

    [Fact]
    public async Task ALoginInTheDefaultFolderLetsItRunAsync()
    {
        await using var home = new TemporaryFolder();
        await File.WriteAllTextAsync(Path.Combine(Directory.CreateDirectory(Path.Combine(home.Path, ".claude")).FullName, ".credentials.json"), "{}", Cancellation);

        Assert.True(new SignIn(new UserHome(home.Path, []), new Dictionary<string, string>()).IsAvailable);
    }
}
