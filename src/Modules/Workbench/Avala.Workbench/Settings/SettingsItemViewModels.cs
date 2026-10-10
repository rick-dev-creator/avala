using System.Globalization;
using Avala.Agents.Contracts.Connections;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Supervision.Contracts;
using Avala.Verification.Contracts;
using Avala.Workbench.Presenting;
using Avala.Workspaces.Contracts;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Settings;

internal interface IRuleFileViewModel
{
    string Path { get; }

    string Status { get; }

    string Commit { get; }

    bool EditedInCheckout { get; }

    bool IsRejected { get; }

    string Summary { get; }

    bool CanEditHere { get; }
}

internal interface IRuleViewModel
{
    string Name { get; }

    string Origin { get; }

    string Kind { get; }

    string Target { get; }

    RuleScope Scope { get; }

    PolicyAnswer Answer { get; }

    int Order { get; }
}

internal interface ICapsViewModel
{
    string Scope { get; }

    string Caps { get; }

    IReadOnlyList<SettingLine> Lines { get; }
}

internal sealed record SettingLine(string Name, string Value);

internal interface ICheckViewModel
{
    string Name { get; }

    string Command { get; }

    string Timeout { get; }
}

internal interface IJobSectionViewModel
{
    string Name { get; }

    string Value { get; }
}

internal interface IMachineConnectionViewModel
{
    string Name { get; }

    string Provider { get; }

    string Source { get; }

    string Origin { get; }

    bool IsDefault { get; }

    bool IsDeclared { get; }

    string Reference { get; }

    string Problem { get; }

    IRelayCommand EditCommand { get; }

    IRelayCommand RemoveCommand { get; }
}

internal sealed class RuleFileViewModel(string path, string status, Option<FileOrigin> origin, string summary) : IRuleFileViewModel
{
    public bool IsRejected { get; } = status.StartsWith("Rejected", StringComparison.Ordinal);

    public string Summary { get; } = summary;

    public string Path { get; } = path;

    public string Status { get; } = status;

    public string Commit { get; } = Amounts.Commit(origin);

    public bool EditedInCheckout { get; } = origin.Match(found => found.EditedInWorktree, () => false);

    public bool CanEditHere { get; init; }
}

internal sealed class RuleViewModel(PolicyRule rule, int order) : IRuleViewModel
{
    public int Order { get; } = order;

    public string Name { get; } = rule.Name;

    public string Origin { get; } = SettingsPhrases.Origin(rule.Origin);

    public string Kind { get; } = rule.Kind.Match(kind => kind.ToString(), () => "any");

    public string Target { get; } = rule.Target.Match(target => target, () => "anything");

    public RuleScope Scope { get; } = rule.Scope;

    public PolicyAnswer Answer { get; } = rule.Answer;
}

internal sealed class CapsViewModel(string scope, string caps, IReadOnlyList<SettingLine> lines) : ICapsViewModel
{
    public IReadOnlyList<SettingLine> Lines { get; } = lines;

    public string Scope { get; } = scope;

    public string Caps { get; } = caps;
}

internal sealed class CheckViewModel(CheckDeclared check) : ICheckViewModel
{
    public string Name { get; } = check.Name;

    public string Command { get; } = check.Command;

    public string Timeout { get; } = $"{Amounts.Seconds(check.Timeout)}s";
}

internal sealed class JobSectionViewModel(string name, string value) : IJobSectionViewModel
{
    public string Name { get; } = name;

    public string Value { get; } = value;
}

internal sealed class MachineConnectionViewModel(DeclaredConnection connection, bool isDefault, IRelayCommand edit, IRelayCommand remove) : IMachineConnectionViewModel
{
    public bool IsDeclared { get; } = connection.Origin == ConnectionOrigin.Declared;

    public string Reference { get; } = connection.Reference.Match(reference => reference, () => string.Empty);

    public IRelayCommand EditCommand { get; } = edit;

    public IRelayCommand RemoveCommand { get; } = remove;

    public string Name { get; } = connection.Name.Value;

    public string Provider { get; } = connection.Provider;

    public string Source { get; } = connection.Source.Match(source => source, () => "the provider's own login");

    public string Origin { get; } = connection.Origin switch
    {
        ConnectionOrigin.Discovered => "discovered on this machine",
        ConnectionOrigin.Implicit => "implicit",
        _ => "declared in connections.json",
    };

    public bool IsDefault { get; } = isDefault;

    public string Problem { get; } = connection.Problem.Match(problem => $"Refused: {ConnectionPhrases.Refused(problem)}", () => string.Empty);
}

internal static class SettingsPhrases
{
    public static string Origin(RuleOrigin origin) => origin switch
    {
        RuleOrigin.BuiltIn => "Built-in",
        RuleOrigin.Repository => "Repository",
        _ => "Session",
    };

    public static string Strategy(FormStrategy strategy) =>
        strategy == FormStrategy.Recommended ? "Recommended options" : "The agent's best judgment";

    public static string Status<TStatus, TError>(TStatus status, Option<TError> error)
        where TStatus : struct, Enum
        where TError : struct, Enum =>
        error.Match(found => $"{status}: {found}", () => status.ToString());

    public static string Opening(FileOpenError error, string path) => error switch
    {
        FileOpenError.Uncreatable => $"{path} does not exist and could not be created.",
        FileOpenError.Unavailable => $"No application is available to open files: edit {path} yourself.",
        _ => $"The platform refused to open {path}.",
    };

    public static string CreatedInRepository(string file) =>
        $"{file} did not exist, so it was created from a minimal template in the repository's working tree. Jobs read it once it is committed.";

    public const string CreatedConnections =
        "connections.json did not exist, so it was created in the data folder with the Auto default. Connections you declare in it apply once Avala starts again.";

    public static string Silence(SupervisionError error) => error switch
    {
        SupervisionError.InvalidSilence => "The window must be more than 0 and at most 86,400 seconds.",
        SupervisionError.Unwritable => "supervision.json could not be written.",
        _ => "The supervision settings were refused.",
    };

    public static string NotSeconds => "Enter the window in seconds.";

    public static IReadOnlyList<SettingLine> Lines(Budgets.Contracts.BudgetCaps caps)
    {
        SettingLine[] lines =
        [
            .. caps.CostPerJob.Select(cost => new SettingLine("Cost per job", Amounts.Costs([cost]))),
            .. caps.TokensPerJob.Match<SettingLine[]>(tokens => [new("Tokens per job", tokens.ToString("N0", CultureInfo.InvariantCulture))], () => []),
            .. caps.HoldAtLimit.Match<SettingLine[]>(hold => [new("Hold when a usage window reaches", Amounts.Percent(hold))], () => []),
            .. caps.MemoryPerJobMegabytes.Match<SettingLine[]>(megabytes => [new("Memory per job", string.Create(CultureInfo.InvariantCulture, $"{megabytes:N0} MB"))], () => []),
            .. caps.CarvePerChild.Match<SettingLine[]>(share => [new("Carved for each sub-agent", Amounts.Percent(share))], () => []),
        ];

        return lines.Length == 0 ? [new SettingLine("No caps", "jobs run without a budget")] : lines;
    }

    public static string Name(string repository)
    {
        var trimmed = repository.Trim().TrimEnd('/', '\\');
        var slash = trimmed.LastIndexOfAny(['/', '\\']);

        return slash < 0 ? trimmed : trimmed[(slash + 1)..];
    }

    public static string Autonomy(string autonomy) => autonomy switch
    {
        "Autonomous" => "Inside the worktree is allowed, outside is denied, forms are answered by policy.",
        "Supervised" => "Anything a rule does not allow asks you first.",
        _ => "The repository declares no autonomy yet.",
    };

    public static string Seconds(TimeSpan window) => window.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);
}
