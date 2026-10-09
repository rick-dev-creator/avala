namespace Avala.ArchitectureTests.Scopes;

internal static class LayerMap
{
    public static IReadOnlyDictionary<string, Placement> Placements { get; } = new Dictionary<string, Placement>(
    [
        .. Module("Avala.Agents", new()
        {
            [""] = Layer.None,
            ["Contracts"] = Layer.Contracts,
            ["Contracts.Events"] = Layer.Contracts,
            ["Contracts.Sessions"] = Layer.Contracts,
            ["Contracts.Connections"] = Layer.Contracts,
            ["Turns"] = Layer.Domain,
            ["Sessions"] = Layer.Application,
            ["Connections"] = Layer.Application,
            ["ConnectionFiles"] = Layer.Infrastructure,
            ["Credentials"] = Layer.Infrastructure,
        }),
        .. Module("Avala.Canvas", new()
        {
            [""] = Layer.None,
            ["Contracts"] = Layer.Contracts,
            ["Canvases"] = Layer.Domain,
            ["Drawing"] = Layer.Application,
            ["Gallery"] = Layer.Application,
            ["Streaming"] = Layer.Application,
            ["Throttling"] = Layer.Application,
        }),
        .. Module("Avala.Jobs", new()
        {
            ["UI"] = Layer.None,
            ["Contracts"] = Layer.Contracts,
            ["Jobs"] = Layer.Domain,
            ["Submission"] = Layer.Application,
            ["Launching"] = Layer.Application,
            ["TurnChecks"] = Layer.Application,
            ["Recovery"] = Layer.Application,
            ["Holding"] = Layer.Application,
            ["Ledger"] = Layer.Application,
            ["Storage"] = Layer.Infrastructure,
            ["JobFiles"] = Layer.Infrastructure,
            ["JobList"] = Layer.ViewModels,
        }),
        .. Module("Avala.Observability", new()
        {
            [""] = Layer.None,
            ["Contracts"] = Layer.Contracts,
            ["Usage"] = Layer.Domain,
            ["Tracking"] = Layer.Application,
            ["Metrics"] = Layer.Infrastructure,
        }),
        .. Module("Avala.Permissions", new()
        {
            [""] = Layer.None,
            ["Contracts"] = Layer.Contracts,
            ["Policies"] = Layer.Domain,
            ["Governance"] = Layer.Application,
            ["Answering"] = Layer.Application,
            ["PolicyFiles"] = Layer.Infrastructure,
        }),
        .. Module("Avala.Supervision", new()
        {
            [""] = Layer.None,
            ["Contracts"] = Layer.Contracts,
            ["Watching"] = Layer.Domain,
            ["Supervising"] = Layer.Application,
            ["Settings"] = Layer.Infrastructure,
        }),
        .. Module("Avala.Budgets", new()
        {
            [""] = Layer.None,
            ["Contracts"] = Layer.Contracts,
            ["Caps"] = Layer.Domain,
            ["Enforcement"] = Layer.Application,
            ["BudgetFiles"] = Layer.Infrastructure,
        }),
        .. Module("Avala.Simulator", new()
        {
            [""] = Layer.None,
            ["Scenarios"] = Layer.Domain,
            ["Playback"] = Layer.Application,
            ["FileSystem"] = Layer.Infrastructure,
            ["Recordings"] = Layer.Infrastructure,
            ["Workloads"] = Layer.Infrastructure,
        }),
        .. Module("Avala.Recording", new()
        {
            [""] = Layer.None,
            ["Recordings"] = Layer.Domain,
            ["Capturing"] = Layer.Application,
            ["Settings"] = Layer.Infrastructure,
            ["Storage"] = Layer.Infrastructure,
        }),
        .. Module("Avala.Verification", new()
        {
            [""] = Layer.None,
            ["Contracts"] = Layer.Contracts,
            ["Checks"] = Layer.Domain,
            ["Verifying"] = Layer.Application,
            ["Evidence"] = Layer.Application,
        }),
        .. Module("Avala.Workspaces", new()
        {
            [""] = Layer.None,
            ["Contracts"] = Layer.Contracts,
            ["Workspaces"] = Layer.Domain,
            ["Provisioning"] = Layer.Application,
            ["BaseFiles"] = Layer.Application,
            ["Git"] = Layer.Infrastructure,
            ["Storage"] = Layer.Infrastructure,
        }),
        .. Module("Avala.Fixtures.Compliant", new()
        {
            ["Contracts"] = Layer.Contracts,
            ["Domain"] = Layer.Domain,
            ["Application"] = Layer.Application,
            ["Infrastructure"] = Layer.Infrastructure,
            ["ViewModels"] = Layer.ViewModels,
        }),
        .. Module("Avala.Fixtures.Violating", new()
        {
            ["Contracts"] = Layer.Contracts,
            ["Domain"] = Layer.Domain,
            ["Application"] = Layer.Application,
            ["Infrastructure"] = Layer.Infrastructure,
            ["ViewModels"] = Layer.ViewModels,
        }),
        .. Module("Avala.Fixtures.Violating.Other", new()
        {
            ["Domain"] = Layer.Domain,
        }),
    ]);

    private static IEnumerable<KeyValuePair<string, Placement>> Module(string module, Dictionary<string, Layer> namespaces) =>
        namespaces.Select(entry => KeyValuePair.Create(
            entry.Key.Length == 0 ? module : $"{module}.{entry.Key}",
            new Placement(module, entry.Value)));
}
