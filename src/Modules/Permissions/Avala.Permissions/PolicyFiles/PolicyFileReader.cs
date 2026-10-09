using System.Text;
using Avala.Permissions.Contracts;
using Avala.Permissions.Governance;
using Avala.Permissions.Policies;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Permissions.PolicyFiles;

internal sealed class PolicyFileReader(IBaseFiles files) : IPolicyFiles
{
    public const int MaximumBytes = 64 * 1024;

    public async ValueTask<PolicyFile> ReadAsync(string workingDirectory, CancellationToken cancellationToken) =>
        (await files.ReadAsync(workingDirectory, PermissionPolicy.PolicyFile, cancellationToken)).Match(
            file => new PolicyFile(file.Origin, file.Content.Match(Parse, Absent)),
            failure => new PolicyFile(Option<FileOrigin>.None, failure == WorkspaceFailure.UnknownWorkspace ? Absent() : PolicyError.Unreadable));

    private static Result<Option<IReadOnlyList<PolicyRule>>, PolicyError> Parse(string text) =>
        Encoding.UTF8.GetByteCount(text) > MaximumBytes
            ? PolicyError.TooLarge
            : PolicyFileParser.Parse(text).Map(Option<IReadOnlyList<PolicyRule>>.Some);

    private static Result<Option<IReadOnlyList<PolicyRule>>, PolicyError> Absent() => Option<IReadOnlyList<PolicyRule>>.None;
}
