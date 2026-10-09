using Avala.Sdk;
using Avala.Workspaces.Contracts;
using Avala.Workspaces.Provisioning;

namespace Avala.Workspaces.WorkingFiles;

internal sealed class WorkingFileStore(IGit git) : IWorkingFiles
{
    public const int MaximumBytes = 64 * 1024;

    public async ValueTask<Result<Option<string>, WorkspaceFailure>> ReadAsync(string repository, string path, CancellationToken cancellationToken) =>
        await (await LocateAsync(repository, path, cancellationToken)).BindAsync(file => ReadTextAsync(file, cancellationToken));

    public async ValueTask<Result<string, WorkspaceFailure>> WriteAsync(string repository, string path, string content, CancellationToken cancellationToken) =>
        content.Length > MaximumBytes
            ? WorkspaceFailure.FileTooLarge
            : await (await LocateAsync(repository, path, cancellationToken)).BindAsync(file => WriteTextAsync(file, content, cancellationToken));

    private async Task<Result<string, WorkspaceFailure>> LocateAsync(string repository, string path, CancellationToken cancellationToken)
    {
        if (!(await git.FindRepositoryRootAsync(repository, cancellationToken)).TryGetValue(out var root, out var failure))
        {
            return failure;
        }

        var full = Path.GetFullPath(Path.Combine(root, path));
        var inside = Path.GetRelativePath(root, full);

        return Path.IsPathRooted(path) || inside.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(inside)
            ? WorkspaceFailure.OutsideRepository
            : full;
    }

    private static async Task<Result<Option<string>, WorkspaceFailure>> ReadTextAsync(string file, CancellationToken cancellationToken)
    {
        if (!File.Exists(file))
        {
            return Option<string>.None;
        }

        try
        {
            await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);

            if (stream.Length > MaximumBytes)
            {
                return WorkspaceFailure.FileTooLarge;
            }

            using var reader = new StreamReader(stream);

            return Option<string>.Some(await reader.ReadToEndAsync(cancellationToken));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return WorkspaceFailure.FileUnreadable;
        }
    }

    private static async Task<Result<string, WorkspaceFailure>> WriteTextAsync(string file, string content, CancellationToken cancellationToken)
    {
        var temporary = $"{file}.{Guid.NewGuid():N}.tmp";

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file) ?? ".");
            await File.WriteAllTextAsync(temporary, content, cancellationToken);
            File.Move(temporary, file, overwrite: true);

            return file;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            File.Delete(temporary);

            return WorkspaceFailure.FileUnwritable;
        }
    }
}
