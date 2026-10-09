using Avala.ClaudeCode.Conversations;

namespace Avala.ClaudeCode.Discovery;

internal sealed class SignIn(UserHome home, IReadOnlyDictionary<string, string> environment)
{
    public static readonly IReadOnlyList<string> CredentialVariables =
    [
        "ANTHROPIC_API_KEY",
        "ANTHROPIC_AUTH_TOKEN",
        "CLAUDE_CODE_OAUTH_TOKEN",
        "CLAUDE_CODE_USE_BEDROCK",
        "CLAUDE_CODE_USE_VERTEX",
    ];

    public bool IsAvailable =>
        CredentialVariables.Any(name => environment.TryGetValue(name, out var value) && value.Length > 0)
        || new LoginFolders(home).HoldsAnyLogin();
}
