using Avala.Sdk;

namespace Avala.Workspaces.Contracts;

public interface IRemoteRepositories
{
    ValueTask<Option<string>> RemoteAsync(string repository, string remote, CancellationToken cancellationToken);

    ValueTask<bool> HasCredentialHelperAsync(string repository, string url, CancellationToken cancellationToken);

    ValueTask<Result<string, WorkspaceFailure>> PublishAsync(string repository, string remote, string url, CancellationToken cancellationToken);

    ValueTask<Result<string, WorkspaceFailure>> CloneAsync(string url, string parent, string name, CancellationToken cancellationToken);
}
