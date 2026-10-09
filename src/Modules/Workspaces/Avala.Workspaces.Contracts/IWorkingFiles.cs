using Avala.Sdk;

namespace Avala.Workspaces.Contracts;

public interface IWorkingFiles
{
    ValueTask<Result<Option<string>, WorkspaceFailure>> ReadAsync(string repository, string path, CancellationToken cancellationToken);

    ValueTask<Result<string, WorkspaceFailure>> WriteAsync(string repository, string path, string content, CancellationToken cancellationToken);
}

public interface IRuleFileFormat
{
    string Path { get; }

    Option<Enum> Rejection(string content);
}
