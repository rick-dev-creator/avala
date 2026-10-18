using Avala.Sdk.Processes;

namespace Avala.Host.Composition;

internal sealed class BoundedStop(IProcessTrees trees, TimeProvider clock)
{
    public static readonly TimeSpan Limit = TimeSpan.FromSeconds(20);

    public async Task<bool> StopAsync(Func<Task> stopping)
    {
        try
        {
            await stopping().WaitAsync(Limit, clock);

            return true;
        }
        catch (TimeoutException)
        {
            foreach (var tree in trees.Open)
            {
                _ = await trees.CloseAsync(tree.Id, CancellationToken.None);
            }

            return false;
        }
    }
}
