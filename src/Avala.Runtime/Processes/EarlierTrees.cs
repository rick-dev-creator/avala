using Avala.Sdk;
using Microsoft.Extensions.Logging;

namespace Avala.Runtime.Processes;

internal sealed partial class EarlierTrees(ProcessTrees trees, ILogger<EarlierTrees> logger) : IStartupTask
{
    public StartupStage Stage => StartupStage.Open;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        foreach (var (tree, reaped) in await trees.ReapEarlierAsync(cancellationToken))
        {
            if (reaped > 0)
            {
                LogReaped(reaped, tree.Value);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ended {Count} processes an earlier run of Avala left running in its process tree {Tree}")]
    private partial void LogReaped(int count, Guid tree);
}
