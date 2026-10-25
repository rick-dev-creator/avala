using Avala.Components.Privacy;
using Avala.Forges.Contracts;
using Avala.Sdk;
using Avala.Verification.Contracts;
using Avala.Workbench.Readying;

namespace Avala.Workbench.Readiness;

internal enum ReadinessNeed
{
    Forge,
    Rules,
    Checks,
}

internal sealed record ReadinessStep(ReadinessNeed Need, PersonalPhrase Text, string Action)
{
    public bool Working { get; init; }

    public string Dismiss { get; init; } = string.Empty;

    public string Command { get; init; } = string.Empty;

    public IReadOnlyList<AccountStatus> Accounts { get; init; } = [];
}

internal static class ReadinessPhrases
{
    public const string Purpose = "to open pull requests and follow their checks";

    public const string Connect = "Connect";

    public const string CheckAgain = "Check again";

    public const string Commit = "Commit";

    public const string DeclareChecks = "Declare checks";

    public const string RulesUncommitted = ".avala/jobs.json has changes jobs do not see until they are committed";

    public const string ChecksMissing = "Without .avala/checks.json Avala cannot verify the work";

    public const string ChecksFinding = "Avala is running this repository's own commands to find how to check the work";

    public const string ChecksReading = "Avala is reading this repository to find how to check the work";

    public const string ChecksProposed = "Avala proposes these commands to check the work. This repository asks you before anything runs, so the job waits until you choose";

    public const string UseChecks = "Use them";

    public const string SkipChecks = "Skip";

    public const string ChecksNotFound = "Avala found no command that checks this repository, so nothing verifies the work";

    public static IReadOnlyList<ReadinessStep> Steps(ReadinessFacts facts, Option<ChecksDiscovery> discovery) =>
    [
        .. Forge(facts.Forge).Match<ReadinessStep[]>(step => [step], () => []),
        .. facts.RulesUncommitted ? [new ReadinessStep(ReadinessNeed.Rules, new PersonalPhrase(RulesUncommitted), Commit)] : Array.Empty<ReadinessStep>(),
        .. facts.ChecksMissing ? Checks(discovery) : [],
    ];

    public static bool FoundChecks(ReadinessFacts facts, Option<ChecksDiscovery> discovery) =>
        facts.ChecksMissing && discovery.Match(found => found.State == DiscoveryState.Found, () => false);

    public static PersonalPhrase Done(ReadinessNeed need, ReadinessFacts facts, Option<ChecksDiscovery> discovery) => need switch
    {
        ReadinessNeed.Forge => new PersonalPhrase($"Connected {facts.Forge.Host} {Purpose}"),
        ReadinessNeed.Rules => new PersonalPhrase("Committed .avala/jobs.json, so new jobs follow it"),
        _ when FoundChecks(facts, discovery) => new PersonalPhrase(Found(discovery.Match<IReadOnlyList<CheckDeclared>>(found => found.Checks, () => []))),
        _ => new PersonalPhrase("Checks declared in .avala/checks.json"),
    };

    public static string Found(IReadOnlyList<CheckDeclared> checks) =>
        $"Avala found how to check this repository: {string.Join(", ", checks.Select(check => check.Name))}";

    private static ReadinessStep[] Checks(Option<ChecksDiscovery> discovery) => discovery.Match<ReadinessStep[]>(
        found => found.State switch
        {
            DiscoveryState.Running => [new ReadinessStep(ReadinessNeed.Checks, new PersonalPhrase(found.Reads ? ChecksReading : ChecksFinding), string.Empty) { Working = true }],
            DiscoveryState.Proposed => [new ReadinessStep(ReadinessNeed.Checks, new PersonalPhrase(ChecksProposed), UseChecks)
            {
                Command = string.Join('\n', found.Checks.Select(check => check.Command)),
                Dismiss = SkipChecks,
            }],
            DiscoveryState.Found => [],
            _ => [new ReadinessStep(ReadinessNeed.Checks, new PersonalPhrase(ChecksNotFound), DeclareChecks)],
        },
        () => [new ReadinessStep(ReadinessNeed.Checks, new PersonalPhrase(ChecksMissing), DeclareChecks)]);

    public static string Tool(AccountStatus account, Option<SignInTool> tool) =>
        account.Source == CredentialSource.Cli
            ? tool.Match(found => found.Command, () => "its CLI")
            : $"${account.Reference.Match(reference => reference, () => "token")}";

    private static Option<ReadinessStep> Forge(ForgeReadiness forge) => forge.Kind switch
    {
        ForgeReadinessKind.Connectable when forge.Accounts.Count == 1 => new ReadinessStep(
            ReadinessNeed.Forge,
            new PersonalPhrase($"Connect {forge.Host} as ", Login(forge.Accounts[0]), $" through {Tool(forge.Accounts[0], forge.SignIn)} {Purpose}"),
            Connect)
        {
            Accounts = forge.Accounts,
        },
        ForgeReadinessKind.Connectable => new ReadinessStep(ReadinessNeed.Forge, new PersonalPhrase($"Choose the account that connects {forge.Host} {Purpose}"), string.Empty)
        {
            Accounts = forge.Accounts,
        },
        ForgeReadinessKind.SignInNeeded => new ReadinessStep(ReadinessNeed.Forge, new PersonalPhrase($"Sign in to {forge.Host} {Purpose}, then check again"), CheckAgain)
        {
            Command = forge.SignIn.Bind(tool => tool.SignIn).Match(command => command, () => string.Empty),
        },
        _ => Option<ReadinessStep>.None,
    };

    public static Personal Login(AccountStatus account) =>
        account.Account.Match(found => Personal.Login(found.Login), () => Personal.Plain(account.Name.Value));
}
