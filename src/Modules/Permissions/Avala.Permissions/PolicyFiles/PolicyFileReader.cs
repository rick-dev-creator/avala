using System.Text;
using Avala.Permissions.Contracts;
using Avala.Permissions.Governance;
using Avala.Permissions.Policies;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Permissions.PolicyFiles;

internal sealed class PolicyFileReader(IBaseFiles files) : IPolicyFiles, IRepositoryPolicies
{
    public const int MaximumBytes = 64 * 1024;

    public async ValueTask<PolicyFile> ReadAsync(string workingDirectory, CancellationToken cancellationToken) =>
        Read(await files.ReadAsync(workingDirectory, PermissionPolicy.PolicyFile, cancellationToken));

    public async ValueTask<RepositoryPolicy> OfRepositoryAsync(string repository, CancellationToken cancellationToken)
    {
        var read = Read(await files.ReadCurrentAsync(repository, PermissionPolicy.PolicyFile, cancellationToken));
        var (policy, file, error) = read.Resolved;

        return new RepositoryPolicy(file, error, policy.Rules, read.Origin)
        {
            Autonomy = policy.Declared,
            Strategy = policy.Strategy,
        };
    }

    private static PolicyFile Read(Result<BaseFile, WorkspaceFailure> read) =>
        read.Match(
            file => new PolicyFile(file.Origin, file.Content.Match(Parse, Absent)),
            failure => new PolicyFile(Option<FileOrigin>.None, failure == WorkspaceFailure.UnknownWorkspace ? Absent() : PolicyError.Unreadable));

    private static Result<Option<PermissionPolicy>, PolicyError> Parse(string text) =>
        Encoding.UTF8.GetByteCount(text) > MaximumBytes
            ? PolicyError.TooLarge
            : PolicyFileParser.Parse(text).Map(Option<PermissionPolicy>.Some);

    private static Result<Option<PermissionPolicy>, PolicyError> Absent() => Option<PermissionPolicy>.None;
}
