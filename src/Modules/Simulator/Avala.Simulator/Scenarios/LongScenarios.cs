using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using static Avala.Simulator.Scenarios.ScenarioCatalog;

namespace Avala.Simulator.Scenarios;

internal static class LongScenarios
{
    public static Scenario LongHaul { get; } = new("long-haul",
    [
        [
            Thought("A long piece of work. ", "A sub-agent writes the release notes first."),
            new CallTool(new ItemId("delegate-notes"), ScenarioCatalog.Delegate, """{ "instruction": "[simulate: notes] Write the release notes" }"""),
            Message("The notes came back ", "with their evidence."),
            .. Bill(2_400, 180, 0.0110m, 0.20),
            new Finish(),
        ],
        [
            Message("Halfway through the to-do list, ", "when the usage window runs out."),
            new ReportUsage(new TokenUsage(9_000, 700, 4_500, 900, 175), new Cost(0.0600m, "USD")),
            new RunOutOfWindow("5h", TimeSpan.FromHours(1)),
        ],
        [
            new WriteFile(new ItemId("todo"), "TODO.md", "# To do\n"),
            Message("Picked up where the window ran out: ", "the to-do list is written."),
            .. Bill(800, 40, 0.0020m, 0.10),
            new Finish(),
        ],
        [
            new WriteFile(new ItemId("fix"), "TODO.md", "# To do\n\n- Keep the build green\n"),
            Message("The checks failed on the to-do list; ", "it is fixed."),
            .. Bill(900, 60, 0.0030m, 0.12),
            new Finish(),
        ],
        [
            Message("Starting the changelog, ", "when the weekly window runs out."),
            new ReportUsage(new TokenUsage(7_000, 500, 3_500, 700, 140), new Cost(0.0450m, "USD")),
            new RunOutOfWindow("7d", TimeSpan.FromDays(3)),
        ],
    ]);

    public static IReadOnlyList<Scenario> All { get; } = [LongHaul];
}
