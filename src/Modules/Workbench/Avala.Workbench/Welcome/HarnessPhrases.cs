using Avala.Agents.Contracts.Connections;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.ModelChoices;
using Avala.Workbench.NewJob;

namespace Avala.Workbench.Welcome;

internal static class HarnessPhrases
{
    public const string Auto = "Auto";

    public const string Default = "Default";

    public const string RunOn = "Run on";

    public static string Following(ConnectionCatalog catalog) =>
        catalog.Error.IsNone && catalog.DefaultMode == DefaultMode.Fixed ? Default : Auto;

    public static string Recommended(Option<ConnectionName> connection) =>
        connection.Match(name => $"Recommended · now {name.Value}", () => "Recommended");

    public static string Label(Option<string> following, Option<ConnectionName> running, ModelPickerViewModel models)
    {
        var connection = running.Match(name => name.Value, () => string.Empty);
        var head = following.Match(rule => connection.Length > 0 ? $"{rule} · {connection}" : rule, () => connection);
        var chosen = models.Chosen;
        var model = string.Join(" · ", new[] { chosen.Model, chosen.Effort }.Where(part => part.IsSome).Select(part => part.Match(found => found, () => string.Empty)));

        return model.Length > 0 ? $"{head} · {model}" : head;
    }

    public static (string Text, bool Attention) Why(ConnectionCatalog catalog, Option<Result<ConnectionPreview, JobRejection>> preview) =>
        catalog.Error.IsSome
            ? ("connections.json is rejected: no job can start", true)
            : preview.Match(
                previewed => previewed.Match(
                    found => found.Route switch
                    {
                        ConnectionRoute.MachineDefault => ("The default connection of this machine", false),
                        ConnectionRoute.Repository => ("Named by the repository", false),
                        ConnectionRoute.Capacity => found.Choice.Match(ByCapacity, () => ("Most capacity left", false)),
                        _ => found.Connection.IsSome ? ("Nothing to compare yet: the first connection", false) : (NewJobPhrases.Unavailable, true),
                    },
                    _ => ("The connections cannot be used: check Settings", true)),
                () => (string.Empty, false));

    private static (string Text, bool Attention) ByCapacity(ConnectionChoice choice)
    {
        var reading = choice.Compared.FirstOrDefault(candidate => candidate.Connection == choice.Connection) is { } chosen ? NewJobPhrases.Reading(chosen) : string.Empty;

        return choice.Compared.All(candidate => candidate.Window.IsNone)
            ? ("No usage reported yet: the first connection", false)
            : choice.Reason == ChoiceReason.AllAtLimit
            ? ($"All at their limit, the least used · {reading}", true)
            : ($"Most capacity left · {reading}", false);
    }
}
