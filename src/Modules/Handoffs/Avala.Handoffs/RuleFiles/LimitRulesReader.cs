using Avala.Handoffs.Contracts;
using Avala.Handoffs.Policy;
using Avala.Handoffs.Watching;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Handoffs.RuleFiles;

internal sealed class LimitRulesReader(IBaseFiles files, AvalaPaths paths) : ILimitRules
{
    public const string JobFile = ".avala/jobs.json";

    public const string MachineFile = "limits.json";

    private Task<LimitRules>? machine;

    public async ValueTask<LimitRules> OfWorktreeAsync(string worktree, CancellationToken cancellationToken) =>
        await (await files.ReadAsync(worktree, JobFile, cancellationToken)).Match(
            file => file.Content.Match(
                text => LimitRulesParser.ParseJobFile(text).Match(
                    declared => declared.Match(Task.FromResult, () => MachineAsync(cancellationToken)),
                    error => Task.FromResult(LimitRules.Rejected(error))),
                () => MachineAsync(cancellationToken)),
            _ => Task.FromResult(LimitRules.Rejected(HandoffError.Unreadable)));

    private Task<LimitRules> MachineAsync(CancellationToken cancellationToken) =>
        LazyInitializer.EnsureInitialized(ref machine, () => ReadAsync(Path.Combine(paths.Data, MachineFile))).WaitAsync(cancellationToken);

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
