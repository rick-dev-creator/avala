using Avala.Workbench.Upkeep;

namespace Avala.Workbench.Resources;

internal static class ResourcePhrases
{
    public static string Leftovers(ResourceState state) =>
        Review.Amounts.Joined([
            .. Count(state.LeftRunning.Count, "process left running", "processes left running"),
            .. Count(state.Stale.Strays.Count + state.Stale.Missing.Count, "stale worktree", "stale worktrees"),
        ]);

    private static string[] Count(int count, string one, string many) => count > 0 ? [Review.Amounts.Count(count, one, many)] : [];
}
