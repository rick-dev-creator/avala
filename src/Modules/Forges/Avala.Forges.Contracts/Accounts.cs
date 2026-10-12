using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Sdk.Processes;

namespace Avala.Forges.Contracts;

public sealed record ForgeConnectionEdit(ForgeName Name, string Forge)
{
    public Option<Uri> Url { get; init; }

    public CredentialSource Source { get; init; }

    public Option<string> Reference { get; init; }
}

public interface IForgeConnections
{
    ValueTask<Result<ForgeCatalog, ForgeError>> ConnectAsync(ForgeConnectionEdit connection, CancellationToken cancellationToken);

    ValueTask<Result<ForgeCatalog, ForgeError>> RemoveAsync(ForgeName name, CancellationToken cancellationToken);
}

public sealed record ForgeConnectionsChanged(ForgeCatalog Catalog) : IIntegrationEvent;

public sealed record DetectedLogin(Uri Url, string Login)
{
    public Option<string> Reference { get; init; }
}

public sealed record ForgeProbe(string Path, string Forge);

public interface IForgeLogins
{
    string Forge { get; }

    Option<CliCall> Logins { get; }

    Option<string> SignInCommand { get; }

    IReadOnlyList<string> TokenVariables { get; }

    Option<ForgeProbe> Probe { get; }

    IReadOnlyList<DetectedLogin> Read(ProcessOutcome outcome);
}

public sealed record ForgeAccount(string Login);

public enum AccountOrigin
{
    Declared,
    Detected,
}

public sealed record AccountStatus(ForgeName Name, string ForgeKind, string Host, CredentialSource Source, Option<ForgeAccount> Account, Option<ForgeError> Problem, AccountOrigin Origin)
{
    public Option<Uri> Url { get; init; }

    public Option<string> Reference { get; init; }
}

public sealed record SignInTool(string Command, IReadOnlyList<string> Forges, Option<string> SignIn, bool Installed);

public sealed record SourceControlAccounts(IReadOnlyList<AccountStatus> Accounts, IReadOnlyList<SignInTool> Tools);

public interface ISourceControlAccounts
{
    ValueTask<SourceControlAccounts> AccountsAsync(CancellationToken cancellationToken);

    ValueTask<Result<IReadOnlyList<RemoteRepository>, ForgeError>> RepositoriesAsync(ForgeName forge, string search, CancellationToken cancellationToken);
}
