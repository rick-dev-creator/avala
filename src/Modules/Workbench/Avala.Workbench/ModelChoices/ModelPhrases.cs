using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;

namespace Avala.Workbench.ModelChoices;

internal static class ModelPhrases
{
    public const string HarnessDefault = "The harness's default";

    public static ModelDefaults Connection { get; } = new(HarnessDefault, HarnessDefault);

    public static ModelDefaults Job(OffersModels offered, ModelChoice repository) =>
        new(Default(repository.Model, offered.DefaultModel), Default(repository.Effort, offered.DefaultEffort));

    public static string JobNote(ModelChoice repository) =>
        repository.IsDefault
            ? "Default is the connection's own choice, from its settings in connections.json, else the harness's."
            : "Default is the repository's choice in .avala/jobs.json, then the connection's.";

    public static string ConnectionNote => "The default every job on this connection runs with, unless the job or its repository chooses another.";

    public static string Following(ConnectionName connection, OffersModels offered, ModelChoice repository)
    {
        var ran = repository.Or(offered.DefaultChoice());

        return $"Auto picks the connection when the job starts and runs with its default: now {connection.Value}, {Ran(ran.Model, ran.Effort, offered.Efforts.Count > 0)}.";
    }

    public static string Unoffered(Option<ConnectionName> connection, bool following) =>
        connection.Match(
            name => following
                ? $"Auto picks the connection when the job starts: now {name.Value}, whose harness offers no choice of model."
                : $"{name.Value} offers no choice of model: its harness runs its own.",
            () => string.Empty);

    public static string Ran(Option<string> model, Option<string> effort, bool offersEffort) =>
        model.Match(found => found, () => "the harness's default model")
        + (offersEffort ? effort.Match(found => $" · {found} effort", () => " · the harness's default effort") : string.Empty);

    public static string Ran(ModelReported reported) =>
        reported.Model + reported.Effort.Match(effort => $" · {effort} effort", () => string.Empty);

    private static string Default(Option<string> repository, Option<string> connection) =>
        repository.Match(
            chosen => $"Default ({chosen}, the repository's)",
            () => connection.Match(chosen => $"Default ({chosen})", () => HarnessDefault));
}
