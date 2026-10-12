using System.Collections.Frozen;
using Avala.Components.Privacy;
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

    private static readonly FrozenDictionary<ForgeError, string> Problems = new Dictionary<ForgeError, string>
    {
        [ForgeError.MissingCredential] = "Token not set",
        [ForgeError.CliUnavailable] = "CLI not installed",
        [ForgeError.Unauthorized] = "Signed out",
        [ForgeError.Unreachable] = "Unreachable",
        [ForgeError.RateLimited] = "Rate limited",
        [ForgeError.UnknownForge] = "Plugin missing",
        [ForgeError.MissingUrl] = "No host",
    }.ToFrozenDictionary();

    public static string SignIn(AccountStatus account, Option<SignInTool> tool) => account.Source switch
    {
        CredentialSource.Cli => tool.Match(found => found.Command, () => "cli"),
        CredentialSource.Environment => Token,
        _ => string.Empty,
    };

    public static string Problem(ForgeError error) => Problems.GetValueOrDefault(error, "Not usable");

    public static PersonalPhrase Cli(CliState state, string cli, string login) => state switch
    {
        CliState.SignedIn => new PersonalPhrase("Signed in as ", Personal.Login(login)),
        CliState.SignedOut => new PersonalPhrase($"{cli} is not signed in"),
        CliState.Missing => new PersonalPhrase($"{cli} is not installed"),
        _ => PersonalPhrase.Empty,
    };

    public static Personal Login(AccountStatus account) =>
        account.Account.Match(found => Personal.Login(found.Login), () => Personal.Plain(account.Name.Value));

    public static string Monogram(string name)
    {
        var letters = name.Split(['-', '_', '.', ' '], StringSplitOptions.RemoveEmptyEntries).Select(part => char.ToUpperInvariant(part[0])).Take(2).ToArray();

        return letters.Length > 0 ? new string(letters) : "?";
    }

    public static string Refusal(ForgeError error) => ForgePhrases.Error(error) is { Length: > 0 } text ? char.ToUpperInvariant(text[0]) + text[1..] : string.Empty;

    public static string TokenVariable(string kind) => $"{kind.ToUpperInvariant().Replace('-', '_')}_TOKEN";
}
