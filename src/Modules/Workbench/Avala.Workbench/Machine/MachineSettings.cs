using Avala.Agents.Contracts.Connections;
using Avala.Resources.Contracts;
using Avala.Sdk;
using Avala.Supervision.Contracts;

namespace Avala.Workbench.Machine;

internal sealed record MachineState(ConnectionCatalog Connections, SupervisionSettings Supervision, ResourceSettings Resources);

internal sealed class MachineSettings(IConnections connections, ISupervision supervision, IResources resources)
{
    public async ValueTask<MachineState> ReadAsync(CancellationToken cancellationToken) =>
        new(
            await connections.CatalogAsync(cancellationToken),
            await supervision.SettingsAsync(cancellationToken),
            await resources.SettingsAsync(cancellationToken));

    public ValueTask<Result<SupervisionSettings, SupervisionError>> ChangeSilenceAsync(TimeSpan silence, CancellationToken cancellationToken) =>
        supervision.ChangeSilenceAsync(silence, cancellationToken);
}

internal sealed class SettingsFiles(IFileOpener opener, AvalaPaths paths)
{
    public const string Connections = "connections.json";

    public ValueTask<Result<string, FileOpenError>> OpenInRepositoryAsync(string repository, string file, CancellationToken cancellationToken) =>
        opener.OpenAsync(Path.Combine(repository, file), cancellationToken);

    public ValueTask<Result<string, FileOpenError>> OpenInDataFolderAsync(string file, CancellationToken cancellationToken) =>
        opener.OpenAsync(Path.Combine(paths.Data, file), cancellationToken);
}
