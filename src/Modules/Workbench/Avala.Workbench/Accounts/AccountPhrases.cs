using Avala.Forges.Contracts;
using Avala.Sdk;
using Avala.Workbench.Presenting;

namespace Avala.Workbench.Accounts;

internal enum AccountState
{
    SignedIn,
    Attention,
    Detected,
}

internal enum CliState
{
    None,
    SignedIn,
    SignedOut,
    Missing,
}

internal static class AccountPhrases
{
    public const string Token = "token";

    public static string SignIn(AccountStatus account, Option<SignInTool> tool) => account.Source switch
    {
        CredentialSource.Cli => tool.Match(found => found.Command, () => "cli"),
        CredentialSource.Environment => Token,
        _ => string.Empty,
    };

    public static string Problem(ForgeError error) => error switch
    {
        ForgeError.MissingCredential => "Token not set",
        ForgeError.CliUnavailable => "CLI not installed",
        ForgeError.Unauthorized => "Signed out",
        ForgeError.Unreachable => "Unreachable",
        ForgeError.RateLimited => "Rate limited",
        ForgeError.UnknownForge => "Plugin missing",
        ForgeError.MissingUrl => "No host",
        _ => "Not usable",
    };

    public static string Cli(CliState state, string cli, string login) => state switch
    {
        CliState.SignedIn => $"Signed in as {login}",
        CliState.SignedOut => $"{cli} is not signed in",
        CliState.Missing => $"{cli} is not installed",
        _ => string.Empty,
    };

    public static string Monogram(string name)
    {
        var letters = name.Split(['-', '_', '.', ' '], StringSplitOptions.RemoveEmptyEntries).Select(part => char.ToUpperInvariant(part[0])).Take(2).ToArray();

        return letters.Length > 0 ? new string(letters) : "?";
    }

    public static string Refusal(ForgeError error) => ForgePhrases.Error(error) is { Length: > 0 } text ? char.ToUpperInvariant(text[0]) + text[1..] : string.Empty;

    public static string TokenVariable(string kind) => $"{kind.ToUpperInvariant().Replace('-', '_')}_TOKEN";
}
