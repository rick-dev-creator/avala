using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;

namespace Avala.Simulator.Scenarios;

internal enum ReplayError
{
    NotFound,
    Unreadable,
    Malformed,
    UnsupportedVersion,
}

internal sealed record RecordedSession(PermissionMode Permissions, IReadOnlyList<IStep> Steps)
{
    public Option<AgentAccount> Account { get; init; }
}

internal static class RecordedScript
{
    public static Scenario Compose(ReplayRequest request, RecordedSession recorded, PermissionMode replayedIn)
    {
        var turns = Turns(recorded.Steps);
        IReadOnlyList<IReadOnlyList<IStep>> scripts = recorded.Permissions != replayedIn
            ? [[new Diverge(Divergence.Permissions(recorded.Permissions, replayedIn))]]
            : [.. turns, [new Diverge(Divergence.ExtraTurn(turns.Count))]];

        return new Scenario(request.Scenario, scripts) { Recorded = true, AsRecorded = request.AsRecorded, Account = recorded.Account };
    }

    public static Scenario Unplayable(ReplayRequest request, ReplayError error) =>
        new(request.Scenario, [[new Diverge(Divergence.Unplayable(request.Recording, error))]]) { Recorded = true };

    private static List<IReadOnlyList<IStep>> Turns(IReadOnlyList<IStep> steps)
    {
        var turns = new List<IReadOnlyList<IStep>>();
        var current = new List<IStep>();

        foreach (var step in steps)
        {
            if (step is Emit { Event: TurnStarted } && current.Any(previous => previous is Emit { Event: TurnStarted }))
            {
                turns.Add(current);
                current = [];
            }

            current.Add(step);
        }

        return current.Count == 0 ? turns : [.. turns, current];
    }
}

internal static class Divergence
{
    public static string Permissions(PermissionMode recorded, PermissionMode replayed) =>
        $"the recording was made in {recorded}, but this session runs in {replayed}";

    public static string ExtraTurn(int turns) =>
        $"the recording holds {turns} turn{(turns == 1 ? string.Empty : "s")}, but the harness started another";

    public static string Unplayable(string recording, ReplayError error) => $"the recording {recording} cannot be replayed: {error}";

    public static string Permission(PermissionDecision recorded, PermissionDecision given) =>
        $"the recording answered the permission for {recorded.Item.Value} with {Describe(recorded)}, but the harness answered {Describe(given)}";

    public static string Answer(FormAnswer recorded, FormAnswer given) =>
        $"the recording answered the form {recorded.Item.Value} with {Describe(recorded)}, but the harness answered {Describe(given)}";

    public static string NeverAsked(ItemId item) => $"the recording answers {item.Value}, which this replay never asked";

    public static string NeverAnswered(ItemId item) => $"the harness answered {item.Value}, which the recording never answered";

    public static string Interrupted => "the harness interrupted the turn, which the recording never did";

    public static bool Same(FormAnswer recorded, FormAnswer given) =>
        recorded.Item == given.Item
        && recorded.Declined == given.Declined
        && recorded.Message == given.Message
        && recorded.Fields.Count == given.Fields.Count
        && recorded.Fields.Zip(given.Fields).All(pair =>
            pair.First.Field == pair.Second.Field
            && pair.First.Chosen.SequenceEqual(pair.Second.Chosen)
            && pair.First.Text == pair.Second.Text
            && pair.First.Confirmed == pair.Second.Confirmed);

    private static string Describe(PermissionDecision decision) =>
        decision.Answer + decision.Message.Match(message => $" \"{message}\"", () => string.Empty);

    private static string Describe(FormAnswer answer) =>
        answer.Declined
            ? "declined" + answer.Message.Match(message => $" \"{message}\"", () => string.Empty)
            : string.Join("; ", answer.Fields.Select(field =>
                $"{field.Field}: {string.Join(", ", [.. field.Chosen, .. field.Text.Match<string[]>(text => [text], () => [])])}{(field.Confirmed ? " (confirmed)" : string.Empty)}"));
}
