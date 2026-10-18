using Avala.Sdk;

namespace Avala.Forges.Contracts;

public enum ForgeReadinessKind
{
    NoRemote,
    Hosted,
    Connectable,
    SignInNeeded,
    Unknown,
}

public sealed record ForgeReadiness(ForgeReadinessKind Kind, string Host)
{
    public Option<PublishTarget> Connection { get; init; }

    public Option<ForgeInfo> Forge { get; init; }

    public IReadOnlyList<AccountStatus> Accounts { get; init; } = [];

    public Option<SignInTool> SignIn { get; init; }
}

public interface IForgeReadiness
{
    ValueTask<ForgeReadiness> OfAsync(string repository, CancellationToken cancellationToken);
}
