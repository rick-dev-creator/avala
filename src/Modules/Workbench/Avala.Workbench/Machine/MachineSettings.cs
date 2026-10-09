using Avala.Agents.Contracts.Connections;
using Avala.Resources.Contracts;
using Avala.Sdk;
using Avala.Supervision.Contracts;
using Avala.Workbench.RepositoryRules;

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

    public async ValueTask<bool> CreateConnectionsFileAsync(CancellationToken cancellationToken) =>
        (await connections.CatalogAsync(cancellationToken)).File == ConnectionFileStatus.Absent
        && (await connections.ChangeDefaultAsync(Option<ConnectionName>.None, cancellationToken)).IsSuccess;

    public ValueTask<Result<ConnectionCatalog, ConnectionError>> ChangeDefaultAsync(Option<ConnectionName> connection, CancellationToken cancellationToken) =>
        connections.ChangeDefaultAsync(connection, cancellationToken);
}

internal sealed class SettingsFiles(IFileOpener opener, AvalaPaths paths)
{
    public const string Connections = "connections.json";

    private static readonly Dictionary<string, string> Templates = new(StringComparer.Ordinal)
    {
        [Connections] = "{\n  \"default\": \"auto\"\n}\n",
        [RuleFiles.Permissions] = "{\n  \"autonomy\": \"supervised\",\n  \"rules\": []\n}\n",
        [RuleFiles.Budget] = "{}\n",
        [RuleFiles.Checks] = "{\n  \"checks\": []\n}\n",
        [RuleFiles.Jobs] = "{}\n",
    };

    public static string TemplateOf(string file) => Templates.GetValueOrDefault(file, "{}\n");

    public string InDataFolder(string file) => Path.Combine(paths.Data, file);

    public ValueTask<Result<OpenedFile, FileOpenError>> OpenInRepositoryAsync(string repository, string file, CancellationToken cancellationToken) =>
        opener.OpenAsync(Path.Combine(repository, file), TemplateOf(file), cancellationToken);

    public ValueTask<Result<OpenedFile, FileOpenError>> OpenInDataFolderAsync(string file, CancellationToken cancellationToken) =>
        opener.OpenAsync(InDataFolder(file), TemplateOf(file), cancellationToken);
}
