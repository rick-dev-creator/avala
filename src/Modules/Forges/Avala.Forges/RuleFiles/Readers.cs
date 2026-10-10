using Avala.Forges.Connecting;
using Avala.Forges.Contracts;
using Avala.Forges.Delivering;
using Avala.Forges.Policy;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Forges.RuleFiles;

internal sealed class ForgeFileReader(AvalaPaths paths) : IForgeFile
{
    private Task<Result<ForgeSettings, ForgeError>>? settings;

    public async ValueTask<Result<ForgeSettings, ForgeError>> ReadAsync(CancellationToken cancellationToken) =>
        await LazyInitializer.EnsureInitialized(ref settings, () => ReadAsync(Path.Combine(paths.Data, ForgeFileParser.File))).WaitAsync(cancellationToken);

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
