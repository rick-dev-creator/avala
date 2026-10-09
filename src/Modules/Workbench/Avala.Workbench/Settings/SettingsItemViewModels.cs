using System.Globalization;
using Avala.Agents.Contracts.Connections;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Supervision.Contracts;
using Avala.Verification.Contracts;
using Avala.Workbench.Presenting;
using Avala.Workspaces.Contracts;

namespace Avala.Workbench.Settings;

internal interface IRuleFileViewModel
{
    string Path { get; }

    string Status { get; }

    string Commit { get; }

    bool EditedInCheckout { get; }
}

internal interface IRuleViewModel
{
    string Name { get; }

    string Origin { get; }

    string Kind { get; }

    string Target { get; }

    RuleScope Scope { get; }

    PolicyAnswer Answer { get; }
}

internal interface ICapsViewModel
{
    string Scope { get; }

    string Caps { get; }
}

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

    bool IsDefault { get; }
}

internal sealed class RuleFileViewModel(string path, string status, Option<FileOrigin> origin) : IRuleFileViewModel
{
    public string Path { get; } = path;

    public string Status { get; } = status;

    public string Commit { get; } = Amounts.Commit(origin);

    public bool EditedInCheckout { get; } = origin.Match(found => found.EditedInWorktree, () => false);
}

internal sealed class RuleViewModel(PolicyRule rule) : IRuleViewModel
{
    public string Name { get; } = rule.Name;

    public string Origin { get; } = SettingsPhrases.Origin(rule.Origin);

    public string Kind { get; } = rule.Kind.Match(kind => kind.ToString(), () => "any");

    public string Target { get; } = rule.Target.Match(target => target, () => "anything");

    public RuleScope Scope { get; } = rule.Scope;

    public PolicyAnswer Answer { get; } = rule.Answer;
}

internal sealed class CapsViewModel(string scope, string caps) : ICapsViewModel
{
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

internal sealed class MachineConnectionViewModel(DeclaredConnection connection, bool isDefault) : IMachineConnectionViewModel
{
    public string Name { get; } = connection.Name.Value;

    public string Provider { get; } = connection.Provider;

    public string Source { get; } = connection.Source.Match(source => source, () => "the provider's own login");

    public bool IsDefault { get; } = isDefault;
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

    public static string Opening(FileOpenError error) => error switch
    {
        FileOpenError.NotFound => "The file does not exist in the checkout yet.",
        FileOpenError.Unavailable => "No application is available to open the file.",
        _ => "The platform refused to open the file.",
    };

    public static string Silence(SupervisionError error) => error switch
    {
        SupervisionError.InvalidSilence => "The window must be more than 0 and at most 86,400 seconds.",
        SupervisionError.Unwritable => "supervision.json could not be written.",
        _ => "The supervision settings were refused.",
    };

    public static string NotSeconds => "Enter the window in seconds.";

    public static string Seconds(TimeSpan window) => window.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);
}
