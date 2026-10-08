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
            ["Turns"] = Layer.Domain,
            ["Sessions"] = Layer.Application,
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
            ["Ledger"] = Layer.Application,
            ["Storage"] = Layer.Infrastructure,
            ["JobList"] = Layer.ViewModels,
        }),
        .. Module("Avala.Simulator", new()
        {
            [""] = Layer.None,
            ["Scenarios"] = Layer.Domain,
            ["Playback"] = Layer.Application,
            ["FileSystem"] = Layer.Infrastructure,
        }),
        .. Module("Avala.Workspaces", new()
        {
            [""] = Layer.None,
            ["Contracts"] = Layer.Contracts,
            ["Workspaces"] = Layer.Domain,
            ["Provisioning"] = Layer.Application,
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
