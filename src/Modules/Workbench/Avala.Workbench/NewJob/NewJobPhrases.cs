using Avala.Agents.Contracts.Connections;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Presenting;
using Avala.Workbench.Usage;

namespace Avala.Workbench.NewJob;

internal static class NewJobPhrases
{
    public const string Auto = "Auto";

    public const string Unavailable = "No connection is available: add one in connections.json from Settings.";

    public static string Following(ConnectionCatalog catalog) =>
        catalog.Error.IsNone && catalog.DefaultMode == DefaultMode.Fixed
            ? catalog.Default.Match(name => $"Default ({name.Value})", () => "Default")
            : Auto;

    public static (string Text, bool Attention) Route(
        ConnectionCatalog catalog,
        Option<Result<ConnectionPreview, JobRejection>> preview,
        Option<ConnectionName> chosen) =>
        chosen.Match(
            name => Named(name, preview),
            () => catalog.Error.Match(
                error => ($"connections.json is rejected ({error}), so no job can start: choose the default connection again in Settings.", true),
                () => catalog.Connections.Count == 0
                    ? (Unavailable, true)
                    : preview.Match(
                        previewed => previewed.Match(found => Following(catalog, found), Refused),
                        () => (string.Empty, false))));

    public static string Reading(CandidateCapacity candidate) =>
        candidate.Window.Match(
            window => $"{Amounts.Percent(candidate.Used)} of the {Lowered(UsagePhrases.Window(window.Window))} used",
            () => "no usage reported yet");

    public static string Rejection(JobRejection rejection) => rejection switch
    {
        JobRejection.EmptyRepository => "Name the repository the job works in.",
        JobRejection.EmptyInstruction => "Write what the agent should do.",
        JobRejection.UnknownConnection => "No connection has that name.",
        JobRejection.UnusableConnection => "That connection cannot be used: its credential or the repository's jobs.json is not usable.",
        JobRejection.WorkspaceUnavailable => "The repository's worktree could not be prepared.",
        JobRejection.AgentUnavailable => "The agent could not start.",
        JobRejection.InvalidRequest => "The request is invalid.",
        JobRejection.InvalidAttemptBudget => "The number of attempts is invalid.",
        _ => "The job was refused.",
    };

    private static (string Text, bool Attention) Named(ConnectionName name, Option<Result<ConnectionPreview, JobRejection>> preview) =>
        preview
            .Bind(previewed => previewed.Match(found => found.Choice, _ => Option<ConnectionChoice>.None))
            .Bind(choice => choice.Compared.FirstOrDefault(candidate => candidate.Connection == name).ToOption())
            .Match(
                candidate => ($"Runs on {name.Value} · {Reading(candidate)}{(candidate.Available ? string.Empty : " · at its limit, the budget may hold the job")}", !candidate.Available),
                () => ($"Runs on {name.Value}, even near its limit", false));

    private static (string Text, bool Attention) Following(ConnectionCatalog catalog, ConnectionPreview previewed)
    {
        var prefix = catalog.DefaultMode == DefaultMode.Auto ? Auto : "Default";
        var connection = previewed.Connection.Match(name => name.Value, () => string.Empty);

        return previewed.Route switch
        {
            ConnectionRoute.Repository => ($"{prefix} → {connection} · named by the repository's .avala/jobs.json", false),
            ConnectionRoute.MachineDefault => ($"Default → {connection} · the default connection of this machine", false),
            ConnectionRoute.Capacity => previewed.Choice.Match(choice => ByCapacity(choice), () => ($"Auto → {connection}", false)),
            _ => previewed.Connection.IsSome
                ? ($"Auto → {connection} · nothing to compare by capacity, so the first connection", false)
                : (Unavailable, true),
        };
    }

    private static (string Text, bool Attention) ByCapacity(ConnectionChoice choice)
    {
        var reading = choice.Compared.FirstOrDefault(candidate => candidate.Connection == choice.Connection) is { } chosen ? Reading(chosen) : "no usage reported yet";

        return choice.Reason == ChoiceReason.AllAtLimit
            ? ($"Auto → {choice.Connection.Value} · all at their limit, least used: {reading} · the budget will hold the job", true)
            : ($"Auto → {choice.Connection.Value} · {reading} · the most capacity left", false);
    }

    private static (string Text, bool Attention) Refused(JobRejection rejection) =>
        rejection == JobRejection.InvalidJobFile
            ? ("The repository's .avala/jobs.json is invalid: the job would fail before it starts.", true)
            : ("The connections cannot be used: check connections.json in Settings.", true);

    private static string Lowered(string phrase) =>
        phrase.Length > 0 && char.IsUpper(phrase[0]) ? $"{char.ToLowerInvariant(phrase[0])}{phrase[1..]}" : phrase;
}
