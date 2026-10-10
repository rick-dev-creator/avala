using System.Globalization;
using Avala.Forges.Contracts;
using Avala.Workbench.Presenting;

namespace Avala.Workbench.Settings;

internal interface IForgeConnectionViewModel
{
    string Name { get; }

    string Forge { get; }

    string Url { get; }

    string Credential { get; }

    string Problem { get; }
}

internal sealed class ForgeConnectionViewModel(ForgeConnectionInfo connection, IReadOnlyList<ForgeInfo> installed) : IForgeConnectionViewModel
{
    public string Name { get; } = connection.Name.Value;

    public string Forge { get; } = installed.FirstOrDefault(forge => forge.Id == connection.Forge) is { } known ? known.Name : connection.Forge;

    public string Url { get; } = connection.Url.Match(url => url.ToString(), () => installed.FirstOrDefault(forge => forge.Id == connection.Forge)?.DefaultUrl.Match(url => url.ToString(), () => string.Empty) ?? string.Empty);

    public string Credential { get; } = ForgeSettingsPhrases.Credential(connection);

    public string Problem { get; } = connection.Problem.Match(problem => $"Not usable: {ForgePhrases.Error(problem)}", () => string.Empty);
}

internal static class ForgeSettingsPhrases
{
    public static string Credential(ForgeConnectionInfo connection) => connection.Source switch
    {
        CredentialSource.Environment => connection.Reference.Match(variable => $"token in ${variable}", () => "token in an environment variable"),
        CredentialSource.Cli => "the forge's own command-line login",
        _ => "no credential",
    };

    public static string File(ForgeCatalog catalog) =>
        catalog.FileError.Match(
            error => $"forges.json is rejected: {ForgePhrases.Error(error)}",
            () => catalog.Connections.Count == 0
                ? "No forge declared in forges.json"
                : string.Create(CultureInfo.InvariantCulture, $"forges.json · checks every {catalog.PollInterval.TotalSeconds:0}s · {string.Join(", ", catalog.Forges.Select(forge => forge.Name))} installed"));
}
