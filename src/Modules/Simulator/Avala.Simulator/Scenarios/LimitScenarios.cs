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
            new ReportContext(141_000, ContextWindow, Compacted: false),
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
            new IfTold("handed off", Told: false, new RunOutOfWindow("7d", TimeSpan.FromDays(3))),
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

    public static Scenario FixPastTheWindow { get; } = new("fix-past-the-window",
    [
        [
            Thought("A quick change ", "to the calculator."),
            new WriteFile(new ItemId("edit"), "calculator.txt", "add(2, 2) = 5 BROKEN\n"),
            Message("The calculator is ready."),
            .. Bill(3_100, 240, 0.0150m, 0.21),
            new Finish(),
        ],
        [
            Thought("The fix needs a long look ", "at every caller."),
            new ReportUsage(new TokenUsage(9_000, 700, 4_500, 900, 175), new Cost(0.0600m, "USD")),
            new RunOutOfWindow("5h", TimeSpan.FromHours(1)),
        ],
        FixedCalculator,
    ]);

    public static Scenario WindDown { get; } = new("wind-down",
    [
        [
            new Recall(new ItemId("told")),
            new IfTold("handed off", Told: false, new ReportLimitResetting("5h", 0.92, TimeSpan.FromHours(1))),
            new IfTold("handed off", Told: false, new AwaitMessage()),
            new WriteFile(new ItemId("edit"), "calculator.txt", "add(2, 2) = 4\n"),
            new IfTold("handed off", Told: false, Message("Wound down: ", "the calculator is in a valid state, and the rest is in this note.")),
            new IfTold("handed off", Told: true, Message("Picked up the relay: ", "the calculator is done.")),
            new ReportUsage(new TokenUsage(3_200, 250, 1_600, 320, 62), new Cost(0.0160m, "USD")),
            new Finish(),
        ],
    ]);

    public static Scenario RelayChain { get; } = new("relay-chain",
    [
        [
            new Recall(new ItemId("told")),
            new IfTold(ChainEnd, Told: false, new ReportLimitResetting("5h", 0.93, TimeSpan.FromHours(2))),
            new IfTold(ChainEnd, Told: false, new AwaitMessage()),
            new WriteFile(new ItemId("edit"), "calculator.txt", "add(2, 2) = 4\n"),
            new IfTold(ChainEnd, Told: false, Message("Wound down ", "and passed the baton.")),
            new IfTold(ChainEnd, Told: true, Message("Took the last leg: ", "the calculator is done.")),
            new ReportUsage(new TokenUsage(3_200, 250, 1_600, 320, 62), new Cost(0.0160m, "USD")),
            new Finish(),
        ],
    ]);

    private const string ChainEnd = "continue it on simulator-three";

    public static IReadOnlyList<Scenario> All { get; } = [NearLimit, SpentWindow, WindowRunsOut, FixPastTheWindow, AccountExhausted, SpentElsewhere, TwoWindowsSpent, WindDown, RelayChain];
}
