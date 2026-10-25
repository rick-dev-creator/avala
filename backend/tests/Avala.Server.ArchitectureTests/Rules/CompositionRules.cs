using Avala.Server.ArchitectureTests.Layout;

namespace Avala.Server.ArchitectureTests.Rules;

internal static class CompositionRules
{
    public static async Task<IReadOnlyList<string>> HostReferencesBesidesModuleEntriesAsync(CancellationToken cancellationToken) =>
        [
            .. (await BackendLayout.ProjectReferencesAsync(BackendLayout.HostProject, cancellationToken))
                .Where(reference => ServerNames.TryParse(reference, out _, out var layer) && layer != Layer.Module),
        ];

    public static async Task<IReadOnlyList<string>> ModulesTheHostLeavesOutAsync(ServerScope scope, CancellationToken cancellationToken)
    {
        var references = await BackendLayout.ProjectReferencesAsync(BackendLayout.HostProject, cancellationToken);

        return [.. scope.In(Layer.Module).Select(entry => entry.Name).Where(entry => !references.Contains(entry))];
    }
}
