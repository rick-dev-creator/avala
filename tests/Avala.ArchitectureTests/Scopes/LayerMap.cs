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
            ["Catalog"] = Layer.Application,
            ["Review"] = Layer.Application,
            ["Delivery"] = Layer.Application,
            ["Storage"] = Layer.Infrastructure,
            ["JobFiles"] = Layer.Infrastructure,
        }),
        .. Module("Avala.Workbench", new()
        {
            ["UI"] = Layer.None,
            ["Timeline"] = Layer.Application,
            ["Board"] = Layer.Application,
            ["Steering"] = Layer.Application,
            ["Replies"] = Layer.Application,
            ["Navigation"] = Layer.ViewModels,
            ["Sidebar"] = Layer.ViewModels,
            ["Conversation"] = Layer.ViewModels,
            ["Cards"] = Layer.ViewModels,
        }),
        .. Module("Avala.Observability", new()
        {
            [""] = Layer.None,
            ["Contracts"] = Layer.Contracts,
            ["Usage"] = Layer.Domain,
            ["Tracking"] = Layer.Application,
            ["Metrics"] = Layer.Infrastructure,
            ["Storage"] = Layer.Infrastructure,
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
            ["Storage"] = Layer.Infrastructure,
        }),
        .. Module("Avala.Budgets", new()
        {
            [""] = Layer.None,
            ["Contracts"] = Layer.Contracts,
            ["Caps"] = Layer.Domain,
            ["Enforcement"] = Layer.Application,
            ["Admission"] = Layer.Application,
            ["BudgetFiles"] = Layer.Infrastructure,
            ["Storage"] = Layer.Infrastructure,
        }),
        .. Module("Avala.Resources", new()
        {
            [""] = Layer.None,
            ["Contracts"] = Layer.Contracts,
            ["Usage"] = Layer.Domain,
            ["Leases"] = Layer.Domain,
            ["Tracking"] = Layer.Application,
            ["Sampling"] = Layer.Application,
            ["Reaping"] = Layer.Application,
            ["Leasing"] = Layer.Application,
            ["Housekeeping"] = Layer.Application,
            ["Settings"] = Layer.Infrastructure,
            ["Disks"] = Layer.Infrastructure,
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
            ["Changes"] = Layer.Application,
            ["Git"] = Layer.Infrastructure,
            ["Storage"] = Layer.Infrastructure,
        }),
        .. Module("Avala.Autopilot", new()
        {
            [""] = Layer.None,
            ["Contracts"] = Layer.Contracts,
            ["Loops"] = Layer.Domain,
            ["Evidence"] = Layer.Domain,
            ["Backlogs"] = Layer.Domain,
            ["Looping"] = Layer.Application,
            ["Approving"] = Layer.Application,
            ["Sourcing"] = Layer.Application,
            ["FollowUps"] = Layer.Application,
            ["RepositoryFiles"] = Layer.Infrastructure,
            ["Storage"] = Layer.Infrastructure,
        }),
        .. Module("Avala.Delegation", new()
        {
            [""] = Layer.None,
            ["Contracts"] = Layer.Contracts,
            ["Policy"] = Layer.Domain,
            ["Delegating"] = Layer.Application,
            ["Reporting"] = Layer.Application,
            ["Records"] = Layer.Application,
            ["RepositoryFiles"] = Layer.Infrastructure,
        }),
        .. Module("Avala.Fixtures.Compliant", new()
        {
            ["Contracts"] = Layer.Contracts,
            ["Domain"] = Layer.Domain,
            ["Application"] = Layer.Application,
            ["Infrastructure"] = Layer.Infrastructure,
            ["ViewModels"] = Layer.ViewModels,
            ["Regions"] = Layer.None,
            ["Scripts"] = Layer.None,
            ["Views"] = Layer.None,
        }),
        .. Module("Avala.Fixtures.Violating", new()
        {
            ["Contracts"] = Layer.Contracts,
            ["Domain"] = Layer.Domain,
            ["Application"] = Layer.Application,
            ["Infrastructure"] = Layer.Infrastructure,
            ["ViewModels"] = Layer.ViewModels,
            ["Regions"] = Layer.None,
            ["Views"] = Layer.None,
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
