namespace Avala.Host.Tests;

internal static class TreeBranches
{
    public static List<Bound> Children(Bound orchestration) =>
        [.. orchestration["Tree"]["Branches"].Items.Where(branch => branch.Kind == "DelegationNodeViewModel")];

    public static Bound Root(Bound orchestration) =>
        orchestration["Tree"]["Branches"].Items.Single(branch => branch.Kind == "OrchestratorCardViewModel");

    public static bool HasRoot(Bound orchestration) =>
        orchestration["Tree"]["Branches"].Items.Any(branch => branch.Kind == "OrchestratorCardViewModel");
}
