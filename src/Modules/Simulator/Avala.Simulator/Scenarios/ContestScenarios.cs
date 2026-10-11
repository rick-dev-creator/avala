using Avala.Agents.Contracts.Sessions;
using static Avala.Simulator.Scenarios.ScenarioCatalog;

namespace Avala.Simulator.Scenarios;

internal static class ContestScenarios
{
    public const string Fixed = "add(2, 2) = 4\n";

    public static Scenario Fails { get; } = new("contest-fails",
    [
        [
            Thought("A quick change ", "to the calculator."),
            new WriteFile(new ItemId("edit"), "calculator.txt", "add(2, 2) = 5 BROKEN\n"),
            Message("The calculator is ready."),
            .. Bill(3_000, 220, 0.0150m, 0.20),
            new Finish(),
        ],
    ]);

    public static Scenario PassesBig { get; } = new("contest-passes-big",
    [
        [
            Thought("I will fix the sum ", "and document every operation while I am here."),
            new WriteFile(new ItemId("edit"), "calculator.txt", Fixed),
            new WriteFile(new ItemId("notes"), "CALCULATOR.md", string.Concat(Enumerable.Range(1, 30).Select(line => $"Operation {line}: documented.\n"))),
            Message("Fixed the sum ", "and documented the calculator."),
            .. Bill(5_200, 640, 0.0120m, 0.22),
            new Finish(),
        ],
    ]);

    public static Scenario PassesSmall { get; } = new("contest-passes-small",
    [
        [
            Thought("Only the sum is wrong."),
            new WriteFile(new ItemId("edit"), "calculator.txt", Fixed),
            Message("Fixed the sum."),
            .. Bill(2_400, 160, 0.0090m, 0.18),
            new Finish(),
        ],
    ]);

    public static Scenario RewritesChecks { get; } = new("contest-rewrites-checks",
    [
        [
            Thought("The checks are in the way. ", "I will empty them and fix the sum."),
            new WriteFile(new ItemId("checks"), ".avala/checks.json", "{ \"checks\": [] }\n"),
            new WriteFile(new ItemId("edit"), "calculator.txt", Fixed),
            Message("Fixed the sum ", "and no check stands in the way."),
            .. Bill(2_600, 180, 0.0080m, 0.19),
            new Finish(),
        ],
    ]);

    public static IReadOnlyDictionary<string, IReadOnlyList<Scenario>> Families { get; } = new Dictionary<string, IReadOnlyList<Scenario>>(StringComparer.OrdinalIgnoreCase)
    {
        ["contest"] = [Fails, PassesBig, PassesSmall],
        ["contest-rewrite"] = [RewritesChecks, PassesBig],
        ["contest-none"] = [Fails],
    };

    public static IReadOnlyList<Scenario> All { get; } = [Fails, PassesBig, PassesSmall, RewritesChecks];
}
