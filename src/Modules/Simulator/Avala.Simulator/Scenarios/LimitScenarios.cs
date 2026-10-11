using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using static Avala.Simulator.Scenarios.ScenarioCatalog;

namespace Avala.Simulator.Scenarios;

internal static class LimitScenarios
{
    public static Scenario NearLimit { get; } = new("near-limit",
    [
        [
            Thought("A long piece of work ", "that uses most of the window."),
            Message("Done, ", "though the usage window is nearly spent."),
            new ReportUsage(new TokenUsage(9_000, 700, 4_500, 900, 175), new Cost(0.0600m, "USD")),
            new ReportLimitResetting("5h", 0.95, TimeSpan.FromSeconds(2)),
            new Finish(),
        ],
    ]);

    public static Scenario SpentWindow { get; } = new("spent-window",
    [
        [
            Thought("A long piece of work ", "that uses most of the window."),
            Message("Still going, ", "though the usage window is nearly spent."),
            new ReportUsage(new TokenUsage(9_000, 700, 4_500, 900, 175), new Cost(0.0600m, "USD")),
            new ReportLimitResetting("5h", 0.95, TimeSpan.FromHours(1)),
        ],
        [
            Message("Picked up where the window ran out."),
            .. Bill(800, 40, 0.0020m, 0.10),
            new Finish(),
        ],
    ]);

    public static Scenario WindowRunsOut { get; } = new("window-runs-out",
    [
        [
            Thought("A long piece of work ", "that runs past the usage window."),
            Message("Halfway there, ", "when the window runs out."),
            new ReportUsage(new TokenUsage(9_000, 700, 4_500, 900, 175), new Cost(0.0600m, "USD")),
            new RunOutOfWindow("5h", TimeSpan.FromHours(1)),
        ],
        [
            Message("Picked up where the window ran out."),
            .. Bill(800, 40, 0.0020m, 0.10),
            new Finish(),
        ],
    ]);

    public static Scenario AccountExhausted { get; } = new("account-exhausted",
    [
        [
            new UnlessTold("handed off", new RunOutOfWindow("7d", TimeSpan.FromDays(3))),
            new Recall(new ItemId("told")),
            new WriteFile(new ItemId("edit"), "calculator.txt", "add(2, 2) = 4\n"),
            Message("Picked up the handed-off work: ", "the calculator is ready."),
            .. Bill(3_900, 310, 0.0190m, 0.10),
            new Finish(),
        ],
    ]);

    public static Scenario SpentElsewhere { get; } = new("spent-elsewhere",
    [
        [
            Message("Done, ", "and this account is nearly spent."),
            new ReportUsage(new TokenUsage(8_000, 600, 4_000, 800, 150), new Cost(0.0500m, "USD")),
            new ReportLimitResetting("5h", 0.97, TimeSpan.FromMinutes(30)),
            new Finish(),
        ],
    ]);

    public static Scenario TwoWindowsSpent { get; } = new("two-windows-spent",
    [
        [
            Thought("A long piece of work ", "that uses both usage windows."),
            Message("Done, ", "though both usage windows are past the threshold."),
            new ReportUsage(new TokenUsage(9_000, 700, 4_500, 900, 175), new Cost(0.0600m, "USD")),
            new ReportLimitResetting("5h", 0.95, TimeSpan.FromHours(1)),
            new ReportLimitResetting("7d", 0.92, TimeSpan.FromDays(2)),
            new Finish(),
        ],
    ]);

    public static IReadOnlyList<Scenario> All { get; } = [NearLimit, SpentWindow, WindowRunsOut, AccountExhausted, SpentElsewhere, TwoWindowsSpent];
}
