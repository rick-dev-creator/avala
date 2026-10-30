using Avala.Handoffs.Contracts;
using Avala.Handoffs.Policy;
using Avala.Handoffs.Watching;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Handoffs.RuleFiles;

internal sealed class LimitRulesReader(IBaseFiles files, AvalaPaths paths) : ILimitRules, ILimitSettings, IAsyncDisposable
{
    public const string JobFile = ".avala/jobs.json";

    public const string MachineFile = "limits.json";

    private readonly SerialExecutor writes = new();
    private Task<LimitRules>? machine;

    string ILimitSettings.MachineFile => MachinePath;

    private string MachinePath => Path.Combine(paths.Data, MachineFile);

    public async ValueTask<LimitRules> OfWorktreeAsync(string worktree, CancellationToken cancellationToken) =>
        await (await files.ReadAsync(worktree, JobFile, cancellationToken)).Match(
            file => file.Content.Match(
                text => LimitRulesParser.ParseJobFile(text).Match(
                    declared => declared.Match(Task.FromResult, () => MachineAsync(cancellationToken)),
                    error => Task.FromResult(LimitRules.Rejected(error))),
                () => MachineAsync(cancellationToken)),
            _ => Task.FromResult(LimitRules.Rejected(HandoffError.Unreadable)));

    async ValueTask<LimitFile> ILimitSettings.MachineAsync(CancellationToken cancellationToken) =>
        (await MachineAsync(cancellationToken)).File();

    public async ValueTask<Result<LimitFile, HandoffError>> ChangeMachineAsync(LimitSettings settings, CancellationToken cancellationToken)
    {
        var text = LimitRulesParser.Text(LimitRules.Of(settings));

        return await LimitRulesParser.ParseMachine(text).Match(
            rules => writes.RunAsync(token => WriteAsync(rules, text, token), cancellationToken),
            error => Task.FromResult(Result<LimitFile, HandoffError>.Failure(error)));
    }

    public ValueTask DisposeAsync() => writes.DisposeAsync();

    private Task<LimitRules> MachineAsync(CancellationToken cancellationToken) =>
        LazyInitializer.EnsureInitialized(ref machine, () => ReadAsync(MachinePath)).WaitAsync(cancellationToken);

    private async Task<Result<LimitFile, HandoffError>> WriteAsync(LimitRules rules, string text, CancellationToken cancellationToken)
    {
        var temporary = $"{MachinePath}.{Guid.NewGuid():N}.tmp";

        try
        {
            Directory.CreateDirectory(paths.Data);
            await File.WriteAllTextAsync(temporary, text, cancellationToken);
            File.Move(temporary, MachinePath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return HandoffError.Unwritable;
        }

        Volatile.Write(ref machine, Task.FromResult(rules));

        return rules.File();
    }

    private static async Task<LimitRules> ReadAsync(string path)
    {
        if (!File.Exists(path))
        {
            return LimitRules.Default;
        }

        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);

            if (stream.Length > LimitRulesParser.MaximumBytes)
            {
                return LimitRules.Rejected(HandoffError.TooLarge);
            }

            using var reader = new StreamReader(stream);

            return LimitRulesParser.ParseMachine(await reader.ReadToEndAsync(CancellationToken.None)).Match(rules => rules, LimitRules.Rejected);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return LimitRules.Rejected(HandoffError.Unreadable);
        }
    }
}
