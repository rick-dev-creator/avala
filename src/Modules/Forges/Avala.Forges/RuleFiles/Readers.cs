using Avala.Forges.Connecting;
using Avala.Forges.Contracts;
using Avala.Forges.Delivering;
using Avala.Forges.Policy;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Forges.RuleFiles;

internal sealed class ForgeFile(AvalaPaths paths) : IForgeFile, IForgeFileWriter, IAsyncDisposable
{
    private readonly SerialExecutor writes = new();
    private Task<Result<ForgeSettings, ForgeError>>? settings;

    private string Location => Path.Combine(paths.Data, ForgeFileParser.File);

    public async ValueTask<Result<ForgeSettings, ForgeError>> ReadAsync(CancellationToken cancellationToken) =>
        await LazyInitializer.EnsureInitialized(ref settings, () => ReadAsync(Location)).WaitAsync(cancellationToken);

    public async ValueTask<Result<ForgeSettings, ForgeError>> ChangeAsync(Func<ForgeSettings, Result<ForgeSettings, ForgeError>> change, CancellationToken cancellationToken) =>
        await writes.RunAsync(token => WriteAsync(change, token), cancellationToken);

    public ValueTask DisposeAsync() => writes.DisposeAsync();

    private async Task<Result<ForgeSettings, ForgeError>> WriteAsync(Func<ForgeSettings, Result<ForgeSettings, ForgeError>> change, CancellationToken cancellationToken)
    {
        if (!(await ReadAsync(Location)).Bind(change).TryGetValue(out var changed, out var error))
        {
            return error;
        }

        var written = Location + ".writing";

        try
        {
            Directory.CreateDirectory(paths.Data);
            await File.WriteAllTextAsync(written, ForgeFileText.Of(changed), cancellationToken);
            File.Move(written, Location, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ForgeError.Unwritable;
        }

        Volatile.Write(ref settings, Task.FromResult(Result<ForgeSettings, ForgeError>.Success(changed)));

        return changed;
    }

    private static async Task<Result<ForgeSettings, ForgeError>> ReadAsync(string path)
    {
        if (!File.Exists(path))
        {
            return ForgeSettings.None;
        }

        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);

            if (stream.Length > ForgeFileParser.MaximumBytes)
            {
                return ForgeError.TooLarge;
            }

            using var reader = new StreamReader(stream);

            return ForgeFileParser.Parse(await reader.ReadToEndAsync(CancellationToken.None));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ForgeError.Unreadable;
        }
    }
}

internal sealed class PullRequestRulesReader(IBaseFiles files) : IPullRequestRules
{
    public async ValueTask<Result<Option<PullRequestRules>, ForgeError>> OfWorktreeAsync(string worktree, CancellationToken cancellationToken) =>
        (await files.ReadAsync(worktree, PullRequestRulesParser.JobFile, cancellationToken)).Match(
            file => file.Content.Match(PullRequestRulesParser.ParseJobFile, () => Option<PullRequestRules>.None),
            _ => Result<Option<PullRequestRules>, ForgeError>.Failure(ForgeError.Unreadable));
}
