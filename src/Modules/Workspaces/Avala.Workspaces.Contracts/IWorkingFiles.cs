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

    Option<RuleFileRejection> Rejection(string content);
}

public sealed record RuleFileRejection(string Module, Enum Error)
{
    public static RuleFileRejection Of<TError>(string module, TError error)
        where TError : struct, Enum => new(module, error);
}
